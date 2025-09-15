using Microsoft.AspNetCore.Mvc;
using FantasyToolbox.Models;
using System.Text.Json;

public class LineupModel : AppPageModel
{
    private readonly IUserService _userService;
    private readonly ILogService _logger;

    public LineupModel(IUserService userService, ILogService logger, IESPNService espnService)
        : base(logger, espnService)
    {
        _userService = userService;
        _logger = logger;
    }

    public List<LineupPlayer> Starters { get; set; } = new();
    public List<LineupPlayer> Bench { get; set; } = new();
    public List<LineupPlayer> IR { get; set; } = new();
    public string? ErrorMessage { get; set; }
    public string? TeamName { get; set; }

    public class LineupPlayer
    {
        public int PlayerId { get; set; }
        public string FullName { get; set; } = "";
        public string Position { get; set; } = "";
        public string ProTeam { get; set; } = "";
        public string Slot { get; set; } = "";
    }

    public async Task<IActionResult> OnGetAsync()
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

            var espnAuth = await GetEspnAuthAsync(user.UserId);
            var leagueData = await GetLeagueDataAsync(user.UserId);

            if (espnAuth == null || leagueData == null)
            {
                ErrorMessage = "ESPN authentication or league data not found.";
                return Page();
            }

            // ESPN API call for roster/lineup
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
            var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}?view=mRoster";
            var response = await httpClient.GetAsync(apiUrl);

            if (!response.IsSuccessStatusCode)
            {
                ErrorMessage = "Unable to fetch lineup data from ESPN.";
                return Page();
            }

            var jsonContent = await response.Content.ReadAsStringAsync();
            ParseLineupData(jsonContent, user.SelectedTeamId.Value);
            //ParseLineupData(jsonContent, user.UserId);
        }
        catch (Exception ex)
        {
            ErrorMessage = "Unable to fetch lineup data. Please try again later.";
            await _logger.LogAsync($"Error fetching lineup for user {userEmail}: {ex.Message}", "Error", ex.ToString());
        }

        return Page();
    }

    private void ParseLineupData(string jsonContent, int userId)
    {
        using var doc = JsonDocument.Parse(jsonContent);

        // Find the user's team roster
        var teams = doc.RootElement.GetProperty("teams");
        JsonElement? myTeam = null;
        foreach (var team in teams.EnumerateArray())
        {
            
            if (team.TryGetProperty("id", out var owner))
            {
                if (owner.GetInt32() == userId)
                {
                    myTeam = team;
                    break;
                }
            }
            if (myTeam != null) break;
        }

        if (myTeam == null) return;

        if (myTeam.Value.TryGetProperty("roster", out var roster) &&
            roster.TryGetProperty("entries", out var entries))
        {
            foreach (var entry in entries.EnumerateArray())
            {
                var player = new LineupPlayer();
                if (entry.TryGetProperty("playerId", out var playerId))
                    player.PlayerId = playerId.GetInt32();

                if (entry.TryGetProperty("playerPoolEntry", out var poolEntry) &&
                    poolEntry.TryGetProperty("player", out var playerInfo))
                {
                    if (playerInfo.TryGetProperty("fullName", out var fullName))
                        player.FullName = fullName.GetString() ?? "";

                    if (playerInfo.TryGetProperty("defaultPositionId", out var positionId))
                        player.Position = MapPositionId(positionId.GetInt32());

                    if (playerInfo.TryGetProperty("proTeamId", out var proTeamId))
                        player.ProTeam = MapProTeamId(proTeamId.GetInt32());
                }

                if (entry.TryGetProperty("lineupSlotId", out var slotId))
                {
                    player.Slot = MapLineupSlotId(slotId.GetInt32());
                    if (player.Slot == "IR")
                        IR.Add(player);
                    else if (player.Slot == "Bench")
                        Bench.Add(player);
                    else
                        Starters.Add(player);
                }
            }
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

    private string MapLineupSlotId(int slotId)
    {
        // ESPN slot IDs (simplified, adjust as needed)
        return slotId switch
        {
            0 => "QB",
            2 => "RB",
            4 => "WR",
            6 => "TE",
            16 => "D/ST",
            17 => "K",
            20 => "Bench",
            21 => "IR",
            _ => "Other"
        };
    }

    private async Task<EspnAuth?> GetEspnAuthAsync(int userId)
    {
        try
        {
            var espnService = HttpContext.RequestServices.GetRequiredService<IESPNService>();
            return await espnService.GetEspnAuthByUserIdAsync(userId);
        }
        catch
        {
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
        catch
        {
            return null;
        }
    }
}