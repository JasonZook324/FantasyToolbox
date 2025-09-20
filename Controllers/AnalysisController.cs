using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using FantasyToolbox.Services;
using FantasyToolbox.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FantasyToolbox.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AnalysisController : ControllerBase
    {
        private readonly GeminiService _geminiService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AnalysisController> _logger;

        public AnalysisController(GeminiService geminiService, ApplicationDbContext context, ILogger<AnalysisController> logger)
        {
            _geminiService = geminiService;
            _context = context;
            _logger = logger;
        }

        [HttpPost("player/{playerId}")]
        public async Task<IActionResult> AnalyzePlayer(string playerId, [FromBody] AnalysisRequest request)
        {
            try
            {
                // Get authenticated user email
                var userEmail = HttpContext.Session.GetString("UserEmail");
                if (string.IsNullOrEmpty(userEmail) && User.Identity?.IsAuthenticated == true)
                {
                    userEmail = User.Identity.Name;
                }

                if (string.IsNullOrEmpty(userEmail))
                {
                    return Unauthorized(new { error = "User authentication required" });
                }

                // Get the user record
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail && u.IsActive);
                if (userRecord == null)
                {
                    return Unauthorized(new { error = "User account not found" });
                }

                // Get ESPN auth for API calls
                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest(new { error = "ESPN authentication or league data not found" });
                }

                // Fetch player data from ESPN API
                using var httpClient = new HttpClient();
                
                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
                var espnRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?view=kona_player_info");
                espnRequest.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
                
                var response = await httpClient.SendAsync(espnRequest);
                
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(500, new { error = "Failed to fetch player data from ESPN" });
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonContent);
                
                // Find the specific player
                object? playerData = null;
                if (doc.RootElement.TryGetProperty("players", out var playersArray))
                {
                    foreach (var player in playersArray.EnumerateArray())
                    {
                        if (player.TryGetProperty("id", out var id) && id.GetInt32().ToString() == playerId)
                        {
                            playerData = JsonSerializer.Deserialize<object>(player.GetRawText());
                            break;
                        }
                    }
                }

                if (playerData == null)
                {
                    return NotFound(new { error = $"Player with ID {playerId} not found" });
                }

                // Generate AI analysis
                var analysisType = request.AnalysisType ?? "general";
                var analysis = await _geminiService.AnalyzePlayerPerformance(playerData, analysisType);

                return Ok(new AnalysisResponse
                {
                    PlayerId = playerId,
                    AnalysisType = analysisType,
                    Analysis = analysis,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing player {PlayerId} for user {UserEmail}", playerId, HttpContext.Session.GetString("UserEmail"));
                return StatusCode(500, new { error = "Failed to analyze player. Please try again." });
            }
        }

        [HttpPost("waiver-wire/contextual")]
        public async Task<IActionResult> AnalyzeContextualWaiverWire([FromBody] ContextualWaiverAnalysisRequest request)
        {
            try
            {
                // Get authenticated user email
                var userEmail = HttpContext.Session.GetString("UserEmail");
                if (string.IsNullOrEmpty(userEmail) && User.Identity?.IsAuthenticated == true)
                {
                    userEmail = User.Identity.Name;
                }

                if (string.IsNullOrEmpty(userEmail))
                {
                    return Unauthorized(new { error = "User authentication required" });
                }

                // Get the user record
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail && u.IsActive);
                if (userRecord == null)
                {
                    return Unauthorized(new { error = "User account not found" });
                }

                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest(new { error = "ESPN authentication or league data not found" });
                }

                // Get the selected player data
                _logger.LogInformation("Searching for player with ID: {PlayerId} for user: {UserEmail}", request.SelectedPlayerId, userEmail);
                var selectedPlayerData = await GetPlayerData(espnAuth, leagueData, request.SelectedPlayerId.ToString());
                if (selectedPlayerData == null)
                {
                    _logger.LogWarning("Player with ID {PlayerId} not found for user {UserEmail}", request.SelectedPlayerId, userEmail);
                    return BadRequest(new { error = $"Selected player not found (ID: {request.SelectedPlayerId}). Please try refreshing the page." });
                }
                
                _logger.LogInformation("Found player data for ID: {PlayerId}", request.SelectedPlayerId);

                // Fetch waiver wire data with position filter
                var waiverWireData = await GetWaiverWireData(espnAuth, leagueData, request.PositionFilter);
                
                // Get NFL schedule data
                var nflScheduleData = await GetNFLScheduleData();

                // Generate contextual analysis
                var analysis = await _geminiService.AnalyzeContextualWaiverWire(
                    selectedPlayerData, 
                    waiverWireData, 
                    nflScheduleData,
                    request.TopN ?? 10
                );

                if (string.IsNullOrEmpty(analysis))
                {
                    return StatusCode(429, new { 
                        error = "AI contextual analysis temporarily unavailable", 
                        message = "The AI analysis service has reached its usage limit. Please wait about a minute and try again, or check the regular waiver wire recommendations instead.",
                        suggestedRetryTime = 60 // seconds
                    });
                }

                return Ok(new ContextualWaiverAnalysisResponse
                {
                    SelectedPlayerName = GetPlayerName(selectedPlayerData),
                    PositionFilter = request.PositionFilter,
                    Analysis = analysis,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing contextual waiver wire for user {UserEmail}", HttpContext.Session.GetString("UserEmail"));
                return StatusCode(500, new { error = "Failed to analyze contextual waiver wire. Please try again." });
            }
        }

        [HttpPost("waiver-wire")]
        public async Task<IActionResult> AnalyzeWaiverWire([FromBody] WaiverAnalysisRequest request)
        {
            try
            {
                // Get authenticated user email
                var userEmail = HttpContext.Session.GetString("UserEmail");
                if (string.IsNullOrEmpty(userEmail) && User.Identity?.IsAuthenticated == true)
                {
                    userEmail = User.Identity.Name;
                }

                if (string.IsNullOrEmpty(userEmail))
                {
                    return Unauthorized(new { error = "User authentication required" });
                }

                // Get the user record
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail && u.IsActive);
                if (userRecord == null)
                {
                    return Unauthorized(new { error = "User account not found" });
                }

                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest(new { error = "ESPN authentication or league data not found" });
                }

                // Fetch waiver wire data
                using var httpClient = new HttpClient();

                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
                
                // Get waiver wire players
                var filterJson = @"{""players"":{""filterStatus"":{""value"":[""FREEAGENT"",""WAIVERS""]},""filterSlotIds"":{""value"":[0,2,4,6,17,16]},""sortPercOwned"":{""sortPriority"":1,""sortAsc"":false},""limit"":50,""offset"":0}}";
                var waiverRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?view=kona_player_info");
                waiverRequest.Headers.Add("X-Fantasy-Filter", filterJson);
                waiverRequest.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");

                var waiverResponse = await httpClient.SendAsync(waiverRequest);
                if (!waiverResponse.IsSuccessStatusCode)
                {
                    return StatusCode(500, new { error = "Failed to fetch waiver wire data" });
                }

                var waiverContent = await waiverResponse.Content.ReadAsStringAsync();
                using var waiverDoc = JsonDocument.Parse(waiverContent);

                var analyses = new List<PlayerAnalysisResult>();

                if (waiverDoc.RootElement.TryGetProperty("players", out var waiverPlayersArray))
                {
                    var topPlayers = waiverPlayersArray.EnumerateArray()
                        .Where(p => !p.TryGetProperty("onTeamId", out var teamId) || teamId.GetInt32() == 0)
                        .Take(request.TopN ?? 10)
                        .ToList();

                    foreach (var player in topPlayers)
                    {
                        try
                        {
                            var playerObj = JsonSerializer.Deserialize<object>(player.GetRawText());
                            var playerId = player.GetProperty("id").GetInt32().ToString();
                            var playerName = "Unknown";
                            
                            if (player.TryGetProperty("player", out var playerInfo) && 
                                playerInfo.TryGetProperty("fullName", out var fullName))
                            {
                                playerName = fullName.GetString() ?? "Unknown";
                            }

                            var analysis = await _geminiService.AnalyzePlayerPerformance(playerObj, "waiver_pickup");

                            analyses.Add(new PlayerAnalysisResult
                            {
                                PlayerId = playerId,
                                PlayerName = playerName,
                                Analysis = analysis
                            });
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to analyze individual waiver wire player");
                        }
                    }
                }

                return Ok(new WaiverAnalysisResponse
                {
                    AnalysisType = "waiver_pickup",
                    PlayerAnalyses = analyses,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error analyzing waiver wire for user {UserEmail}", HttpContext.Session.GetString("UserEmail"));
                return StatusCode(500, new { error = "Failed to analyze waiver wire. Please try again." });
            }
        }

        private async Task<object?> GetPlayerData(EspnAuth espnAuth, FLeagueData leagueData, string playerId)
    {
        using var httpClient = new HttpClient();
        var espnRequest = new HttpRequestMessage(HttpMethod.Get, 
            $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}?view=mRoster");
        espnRequest.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
        
        var response = await httpClient.SendAsync(espnRequest);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("ESPN API request failed with status: {StatusCode}", response.StatusCode);
            return null;
        }

        var jsonContent = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(jsonContent);
        
        _logger.LogInformation("Looking for player ID {PlayerId} in roster data", playerId);
        
        // Look through all teams and their roster entries to find the player
        if (doc.RootElement.TryGetProperty("teams", out var teamsArray))
        {
            _logger.LogInformation("Found {TeamCount} teams in league data", teamsArray.GetArrayLength());
            
            foreach (var team in teamsArray.EnumerateArray())
            {
                if (team.TryGetProperty("roster", out var roster) &&
                    roster.TryGetProperty("entries", out var entries))
                {
                    _logger.LogInformation("Team has {EntryCount} roster entries", entries.GetArrayLength());
                    
                    foreach (var entry in entries.EnumerateArray())
                    {
                        if (entry.TryGetProperty("playerId", out var entryPlayerId))
                        {
                            var entryPlayerIdStr = entryPlayerId.GetInt32().ToString();
                            _logger.LogDebug("Checking player ID: {EntryPlayerId} against target: {TargetPlayerId}", entryPlayerIdStr, playerId);
                            
                            if (entryPlayerIdStr == playerId)
                            {
                                _logger.LogInformation("Found matching player ID: {PlayerId}", playerId);
                                // Return both the roster entry and player info if available
                                var result = new
                                {
                                    playerId = entryPlayerId.GetInt32(),
                                    entry = JsonSerializer.Deserialize<object>(entry.GetRawText()),
                                    player = entry.TryGetProperty("playerPoolEntry", out var playerPool) && 
                                             playerPool.TryGetProperty("player", out var playerInfo) 
                                             ? JsonSerializer.Deserialize<object>(playerInfo.GetRawText()) : null
                                };
                                return result;
                            }
                        }
                    }
                }
            }
        }
        else
        {
            _logger.LogWarning("No 'teams' property found in ESPN response");
        }
        
        _logger.LogWarning("Player ID {PlayerId} not found in any team roster", playerId);
        return null;
    }

    private async Task<object?> GetWaiverWireData(EspnAuth espnAuth, FLeagueData leagueData, string? positionFilter)
    {
        using var httpClient = new HttpClient();
        var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
        
        // Build filter based on position
        var slotIds = positionFilter?.ToUpper() switch
        {
            "QB" => "[0]",
            "RB" => "[2]", 
            "WR" => "[4]",
            "TE" => "[6]",
            "K" => "[17]",
            "D/ST" => "[16]",
            _ => "[0,2,4,6,17,16]"
        };

        var filterJson = $@"{{""players"":{{""filterStatus"":{{""value"":[""FREEAGENT"",""WAIVERS""]}},""filterSlotIds"":{{""value"":{slotIds}}},""sortPercOwned"":{{""sortPriority"":1,""sortAsc"":false}},""limit"":50,""offset"":0}}}}";
        
        var waiverRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?view=kona_player_info");
        waiverRequest.Headers.Add("X-Fantasy-Filter", filterJson);
        waiverRequest.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");

        var waiverResponse = await httpClient.SendAsync(waiverRequest);
        if (!waiverResponse.IsSuccessStatusCode) return null;

        var waiverContent = await waiverResponse.Content.ReadAsStringAsync();
        using var waiverDoc = JsonDocument.Parse(waiverContent);

        return JsonSerializer.Deserialize<object>(waiverDoc.RootElement.GetRawText());
    }

    private async Task<object?> GetNFLScheduleData()
    {
        try
        {
            using var httpClient = new HttpClient();
            
            // Get current week's NFL schedule from ESPN
            var response = await httpClient.GetAsync("https://site.api.espn.com/apis/site/v2/sports/football/nfl/scoreboard");
            
            if (response.IsSuccessStatusCode)
            {
                var jsonContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(jsonContent);
                return JsonSerializer.Deserialize<object>(doc.RootElement.GetRawText());
            }
            
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching NFL schedule data");
            return null;
        }
    }

    private string GetPlayerName(object? playerData)
    {
        if (playerData == null) return "Unknown";
        
        try
        {
            var json = JsonSerializer.Serialize(playerData);
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("player", out var playerInfo) &&
                playerInfo.TryGetProperty("fullName", out var fullName))
            {
                return fullName.GetString() ?? "Unknown";
            }
            
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
    }

    public class AnalysisRequest
    {
        public string? AnalysisType { get; set; } // "general", "waiver_pickup", "start_sit", "trade_value"
    }

    public class WaiverAnalysisRequest
    {
        public int? TopN { get; set; } = 10;
    }

    public class AnalysisResponse
    {
        public string PlayerId { get; set; } = "";
        public string AnalysisType { get; set; } = "";
        public string Analysis { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
    }

    public class WaiverAnalysisResponse
    {
        public string AnalysisType { get; set; } = "";
        public List<PlayerAnalysisResult> PlayerAnalyses { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
    }

    public class PlayerAnalysisResult
    {
        public string PlayerId { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public string Analysis { get; set; } = "";
    }

    public class ContextualWaiverAnalysisRequest
    {
        public int SelectedPlayerId { get; set; }
        public string? PositionFilter { get; set; }
        public int? TopN { get; set; } = 10;
    }

    public class ContextualWaiverAnalysisResponse
    {
        public string SelectedPlayerName { get; set; } = "";
        public string? PositionFilter { get; set; }
        public string Analysis { get; set; } = "";
        public DateTime GeneratedAt { get; set; }
    }
}