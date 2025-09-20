using Microsoft.AspNetCore.Mvc;
using FantasyToolbox.Services;
using FantasyToolbox.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace FantasyToolbox.Controllers
{
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
                // Get the user context (using first active user for now)
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.IsActive);
                if (userRecord == null)
                {
                    return BadRequest("User not found");
                }

                // Get ESPN auth for API calls
                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest("ESPN authentication or league data not found");
                }

                // Fetch player data from ESPN API
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");

                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
                var response = await httpClient.GetAsync($"{apiUrl}?view=kona_player_info");
                
                if (!response.IsSuccessStatusCode)
                {
                    return StatusCode(500, "Failed to fetch player data from ESPN");
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
                    return NotFound($"Player with ID {playerId} not found");
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
                _logger.LogError(ex, "Error analyzing player {PlayerId}", playerId);
                return StatusCode(500, new { error = $"Failed to analyze player: {ex.Message}" });
            }
        }

        [HttpPost("waiver-wire")]
        public async Task<IActionResult> AnalyzeWaiverWire([FromBody] WaiverAnalysisRequest request)
        {
            try
            {
                // Get the user context
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.IsActive);
                if (userRecord == null)
                {
                    return BadRequest("User not found");
                }

                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest("ESPN authentication or league data not found");
                }

                // Fetch waiver wire data
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");

                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
                
                // Get waiver wire players
                var filterJson = @"{""players"":{""filterStatus"":{""value"":[""FREEAGENT"",""WAIVERS""]},""filterSlotIds"":{""value"":[0,2,4,6,17,16]},""sortPercOwned"":{""sortPriority"":1,""sortAsc"":false},""limit"":50,""offset"":0}}";
                var waiverRequest = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?view=kona_player_info");
                waiverRequest.Headers.Add("X-Fantasy-Filter", filterJson);
                waiverRequest.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");

                var waiverResponse = await httpClient.SendAsync(waiverRequest);
                if (!waiverResponse.IsSuccessStatusCode)
                {
                    return StatusCode(500, "Failed to fetch waiver wire data");
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
                _logger.LogError(ex, "Error analyzing waiver wire");
                return StatusCode(500, new { error = $"Failed to analyze waiver wire: {ex.Message}" });
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
}