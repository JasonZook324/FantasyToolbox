using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text;
using FantasyToolbox.Models;

public class WaiverWireModel : AppPageModel
{
    private readonly IUserService _userService;
    private readonly ILogService _logger;

    public WaiverWireModel(IUserService userService, ILogService logger, IESPNService espnService)
        : base(logger, espnService)
    {
        _userService = userService;
        _logger = logger;
    }

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? SelectedPosition { get; set; }
    public bool IsEspnConnected { get; set; }
    public List<WaiverWirePlayer> WaiverWirePlayers { get; set; } = new();

    public class WaiverWirePlayer
    {
        public int PlayerId { get; set; }
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string ProTeam { get; set; } = "";
        public double OwnershipPercentage { get; set; }
        public double FantasyPoints { get; set; }
        public double ProjectedPoints { get; set; }
        public int Rank { get; set; }
    }

    public async Task<IActionResult> OnGetAsync(string? position = null)
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        
        // Check if user is authenticated (similar to Dashboard pattern)
        if (string.IsNullOrEmpty(userEmail) && User.Identity?.IsAuthenticated == true)
        {
            userEmail = User.Identity.Name;
        }
        
        if (string.IsNullOrEmpty(userEmail))
        {
            HttpContext.Session.Clear();
            return RedirectToPage("/Login", new { message = "Please log in to access waiver wire data." });
        }

        SelectedPosition = position;
        
        // Check ESPN connection status
        var espnConnectedStatus = HttpContext.Session.GetString("EspnConnected");
        IsEspnConnected = espnConnectedStatus == "true";
        
        if (!IsEspnConnected)
        {
            return Page();
        }

        try
        {
            // Get user's ESPN credentials and league info
            var user = await _userService.GetUserByEmailAsync(userEmail);
            if (user == null)
            {
                ErrorMessage = "User not found.";
                return Page();
            }

            await FetchWaiverWireDataAsync(user.UserId);
            
            // Apply position filter if specified
            if (!string.IsNullOrEmpty(SelectedPosition))
            {
                WaiverWirePlayers = WaiverWirePlayers
                    .Where(p => p.Position.Equals(SelectedPosition, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to fetch waiver wire data. Please try again later.";
            await _logger.LogAsync($"Error fetching waiver wire data for user {userEmail}: {ex.Message}", "Error", ex.ToString());
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExportCsvAsync(string? position = null)
    {
        var userEmail = HttpContext.Session.GetString("UserEmail");
        
        if (string.IsNullOrEmpty(userEmail))
        {
            return RedirectToPage("/Login");
        }

        try
        {
            var user = await _userService.GetUserByEmailAsync(userEmail);
            if (user == null)
            {
                ErrorMessage = "User not found.";
                return Page();
            }

            await FetchWaiverWireDataAsync(user.UserId);
            
            // Apply position filter if specified
            var playersToExport = WaiverWirePlayers;
            if (!string.IsNullOrEmpty(position))
            {
                playersToExport = WaiverWirePlayers
                    .Where(p => p.Position.Equals(position, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            // Generate CSV content
            var csv = GenerateCsv(playersToExport);
            var fileName = $"waiver_wire_{(!string.IsNullOrEmpty(position) ? position + "_" : "")}{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", fileName);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to export waiver wire data. Please try again.";
            await _logger.LogAsync($"Error exporting waiver wire CSV for user {userEmail}: {ex.Message}", "Error", ex.ToString());
            return Page();
        }
    }

    private async Task FetchWaiverWireDataAsync(int userId)
    {
        using var httpClient = new HttpClient();
        
        // Get ESPN auth data from database
        var espnAuth = await GetEspnAuthAsync(userId);
        var leagueData = await GetLeagueDataAsync(userId);
        
        if (espnAuth == null || leagueData == null)
        {
            throw new InvalidOperationException("ESPN authentication or league data not found.");
        }

        // ESPN API call for waiver wire/free agents - using players_wl view to get all available players
        var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}?view=players_wl&view=kona_playercard&scoringPeriodId=0";
        
        httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
        
        var response = await httpClient.GetAsync(apiUrl);
        
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"ESPN API returned {response.StatusCode}: {response.ReasonPhrase}");
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        await ParseWaiverWireData(jsonContent);
    }

    private async Task ParseWaiverWireData(string jsonContent)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonContent);
            var players = new List<WaiverWirePlayer>();
            
            if (doc.RootElement.TryGetProperty("players", out var playersArray))
            {
                foreach (var playerElement in playersArray.EnumerateArray())
                {
                    try
                    {
                        // Skip players who are on rosters (only show free agents)
                        if (playerElement.TryGetProperty("onTeamId", out var onTeamId) && onTeamId.GetInt32() > 0)
                            continue;

                        var player = new WaiverWirePlayer();

                        if (playerElement.TryGetProperty("id", out var playerId))
                            player.PlayerId = playerId.GetInt32();

                        if (playerElement.TryGetProperty("player", out var playerInfo))
                        {
                            if (playerInfo.TryGetProperty("fullName", out var fullName))
                                player.FullName = fullName.GetString() ?? "";

                            if (playerInfo.TryGetProperty("defaultPositionId", out var positionId))
                            {
                                player.Position = MapPositionId(positionId.GetInt32());
                            }

                            if (playerInfo.TryGetProperty("proTeamId", out var proTeamId))
                            {
                                player.ProTeam = MapProTeamId(proTeamId.GetInt32());
                            }

                            if (playerInfo.TryGetProperty("ownership", out var ownership))
                            {
                                if (ownership.TryGetProperty("percentOwned", out var percentOwned))
                                    player.OwnershipPercentage = Math.Round(percentOwned.GetDouble(), 1);
                            }

                            if (playerInfo.TryGetProperty("stats", out var stats))
                            {
                                // Get current season fantasy points and projected points
                                foreach (var statElement in stats.EnumerateArray())
                                {
                                    if (statElement.TryGetProperty("seasonId", out var seasonId) && 
                                        statElement.TryGetProperty("statSourceId", out var statSourceId))
                                    {
                                        if (statSourceId.GetInt32() == 0) // Actual stats
                                        {
                                            if (statElement.TryGetProperty("appliedTotal", out var appliedTotal))
                                                player.FantasyPoints = Math.Round(appliedTotal.GetDouble(), 1);
                                        }
                                        else if (statSourceId.GetInt32() == 1) // Projected stats
                                        {
                                            if (statElement.TryGetProperty("appliedTotal", out var projectedTotal))
                                                player.ProjectedPoints = Math.Round(projectedTotal.GetDouble(), 1);
                                        }
                                    }
                                }
                            }
                        }

                        if (!string.IsNullOrEmpty(player.FullName))
                        {
                            players.Add(player);
                        }
                    }
                    catch (Exception ex)
                    {
                        await _logger.LogAsync($"Error parsing individual player data: {ex.Message}", "Warning");
                        // Continue processing other players
                    }
                }
            }

            // Sort by projected points descending and assign ranks
            WaiverWirePlayers = players
                .OrderByDescending(p => p.ProjectedPoints)
                .ThenByDescending(p => p.FantasyPoints)
                .ThenBy(p => p.OwnershipPercentage)
                .Select((player, index) => 
                {
                    player.Rank = index + 1;
                    return player;
                })
                .ToList();
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Error parsing waiver wire JSON: {ex.Message}", "Error", ex.ToString());
            throw;
        }
    }

    private string MapPositionId(int positionId)
    {
        return positionId switch
        {
            1 => "QB",
            2 => "RB",
            3 => "WR",
            4 => "TE",
            5 => "K",
            16 => "D/ST",
            _ => "UNKNOWN"
        };
    }

    private string MapProTeamId(int proTeamId)
    {
        // ESPN team ID mappings - simplified version
        return proTeamId switch
        {
            1 => "ATL", 2 => "BUF", 3 => "CHI", 4 => "CIN", 5 => "CLE", 6 => "DAL", 
            7 => "DEN", 8 => "DET", 9 => "GB", 10 => "TEN", 11 => "IND", 12 => "KC", 
            13 => "LV", 14 => "LAR", 15 => "MIA", 16 => "MIN", 17 => "NE", 18 => "NO", 
            19 => "NYG", 20 => "NYJ", 21 => "PHI", 22 => "ARI", 23 => "PIT", 24 => "LAC", 
            25 => "SF", 26 => "SEA", 27 => "TB", 28 => "WSH", 29 => "CAR", 30 => "JAX",
            33 => "BAL", 34 => "HOU",
            _ => "FA"
        };
    }

    private string GenerateCsv(List<WaiverWirePlayer> players)
    {
        var csv = new StringBuilder();
        csv.AppendLine("Rank,Player Name,Position,Team,Ownership %,Fantasy Points,Projected Points");
        
        foreach (var player in players)
        {
            var sanitizedName = SanitizeCsvField(player.FullName);
            var sanitizedPosition = SanitizeCsvField(player.Position);
            var sanitizedTeam = SanitizeCsvField(player.ProTeam);
            
            csv.AppendLine($"{player.Rank},\"{sanitizedName}\",\"{sanitizedPosition}\",\"{sanitizedTeam}\",{player.OwnershipPercentage},{player.FantasyPoints},{player.ProjectedPoints}");
        }
        
        return csv.ToString();
    }

    private string SanitizeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field))
            return field;
        
        // Prevent CSV injection by escaping cells that start with formula characters
        if (field.StartsWith("=") || field.StartsWith("+") || field.StartsWith("-") || field.StartsWith("@"))
        {
            return "'" + field; // Prefix with single quote to neutralize formula
        }
        
        // Escape any existing quotes in the field
        return field.Replace("\"", "\"\"");
    }

    private async Task<EspnAuth?> GetEspnAuthAsync(int userId)
    {
        try
        {
            var espnService = HttpContext.RequestServices.GetRequiredService<IESPNService>();
            return await espnService.GetEspnAuthByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Error getting ESPN auth for user {userId}: {ex.Message}", "Error");
            return null;
        }
    }

    private async Task<FLeagueData?> GetLeagueDataAsync(int userId)
    {
        try
        {
            var espnService = HttpContext.RequestServices.GetRequiredService<IESPNService>();
            return await espnService.GetLeagueDataByUserIdAsync(userId);
        }
        catch (Exception ex)
        {
            await _logger.LogAsync($"Error getting league data for user {userId}: {ex.Message}", "Error");
            return null;
        }
    }
}