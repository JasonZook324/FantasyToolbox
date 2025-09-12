using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using FantasyToolbox.Models;

namespace FantasyToolbox.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LeaguesController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public LeaguesController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{leagueId}/waiver-wire")]
        public async Task<IActionResult> GetWaiverWire(string leagueId, [FromQuery] string userId = "default-user")
        {
            try
            {
                // Get any available user for testing (first active user in database)
                var userRecord = await _context.Users.FirstOrDefaultAsync(u => u.IsActive);
                if (userRecord == null)
                {
                    return BadRequest("No active users found in database");
                }

                // Get ESPN auth and league data
                var espnAuth = await _context.EspnAuth.FirstOrDefaultAsync(e => e.UserId == userRecord.UserId);
                var leagueData = await _context.FLeagueData.FirstOrDefaultAsync(f => f.UserId == userRecord.UserId);

                if (espnAuth == null || leagueData == null)
                {
                    return BadRequest("ESPN authentication or league data not found");
                }

                using var httpClient = new HttpClient();
                
                // Enhanced ESPN API call with X-Fantasy-Filter for more players
                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/games/ffl/seasons/{leagueData.LeagueYear}/segments/0/leagues/{leagueData.LeagueId}";
                
                httpClient.DefaultRequestHeaders.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
                
                var allPlayers = new List<object>();
                int pageSize = 100;
                int offset = 0;
                bool hasMoreData = true;

                // Paginate through waiver wire players
                while (hasMoreData && allPlayers.Count < 300)
                {
                    // Add X-Fantasy-Filter header with correct ESPN slot IDs (QB=0, RB=2, WR=4, TE=6, K=17, DEF=16)
                    var filterJson = $@"{{""players"":{{""filterStatus"":{{""value"":[""FREEAGENT"",""WAIVERS""]}},""filterSlotIds"":{{""value"":[0,2,4,6,17,16]}},""sortPercOwned"":{{""sortPriority"":1,""sortAsc"":false}},""limit"":{pageSize},""offset"":{offset}}}}}";
                    
                    var request = new HttpRequestMessage(HttpMethod.Get, $"{apiUrl}?view=kona_player_info");
                    request.Headers.Add("X-Fantasy-Filter", filterJson);
                    request.Headers.Add("Cookie", $"SWID={espnAuth.Swid}; espn_s2={espnAuth.EspnS2}");
                    // Don't copy default headers to avoid duplicate Cookie header

                    var response = await httpClient.SendAsync(request);
                    if (!response.IsSuccessStatusCode)
                    {
                        // Fallback to basic call if X-Fantasy-Filter fails
                        var basicResponse = await httpClient.GetAsync($"{apiUrl}?view=kona_player_info");
                        if (basicResponse.IsSuccessStatusCode)
                        {
                            var basicContent = await basicResponse.Content.ReadAsStringAsync();
                            using var basicDoc = JsonDocument.Parse(basicContent);
                            if (basicDoc.RootElement.TryGetProperty("players", out var basicPlayersArray))
                            {
                                var basicPlayers = basicPlayersArray.EnumerateArray()
                                    .Where(p => !p.TryGetProperty("onTeamId", out var teamId) || teamId.GetInt32() == 0)
                                    .Select(p => JsonSerializer.Deserialize<object>(p.GetRawText()))
                                    .ToList();
                                
                                return Ok(new { 
                                    players = basicPlayers,
                                    total = basicPlayers.Count,
                                    leagueId = leagueId,
                                    fallback = true
                                });
                            }
                        }
                        return StatusCode(500, "Failed to fetch waiver wire data");
                    }

                    var jsonContent = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonContent);
                    
                    if (doc.RootElement.TryGetProperty("players", out var playersArray))
                    {
                        var freeAgents = playersArray.EnumerateArray()
                            .Where(p => !p.TryGetProperty("onTeamId", out var teamId) || teamId.GetInt32() == 0)
                            .Select(p => JsonSerializer.Deserialize<object>(p.GetRawText()))
                            .ToList();
                        
                        if (freeAgents.Count == 0)
                        {
                            hasMoreData = false;
                        }
                        else
                        {
                            allPlayers.AddRange(freeAgents);
                            offset += pageSize;
                        }
                    }
                    else
                    {
                        hasMoreData = false;
                    }
                }

                return Ok(new { 
                    players = allPlayers,
                    total = allPlayers.Count,
                    leagueId = leagueId
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to fetch waiver wire: {ex.Message}" });
            }
        }

        [HttpGet("{leagueId}/waiver-wire/export")]
        public async Task<IActionResult> ExportWaiverWire(string leagueId)
        {
            try
            {
                var waiverData = await GetWaiverWire(leagueId);
                if (waiverData is OkObjectResult okResult && okResult.Value != null)
                {
                    var data = okResult.Value;
                    var playersProperty = data.GetType().GetProperty("players");
                    var players = playersProperty?.GetValue(data) as List<object>;

                    if (players != null)
                    {
                        var csv = GeneratePlayersCsv(players);
                        var bytes = System.Text.Encoding.UTF8.GetBytes(csv);
                        return File(bytes, "text/csv", $"waiver_wire_{leagueId}_{DateTime.Now:yyyyMMdd}.csv");
                    }
                }
                
                return BadRequest("No waiver wire data available");
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to export waiver wire: {ex.Message}" });
            }
        }

        [HttpGet("{leagueId}/roster-export")]
        public async Task<IActionResult> ExportRosters(string leagueId)
        {
            // Placeholder for roster export functionality
            return Ok(new { message = "Roster export functionality coming soon" });
        }

        private string GeneratePlayersCsv(List<object> players)
        {
            var csv = new System.Text.StringBuilder();
            csv.AppendLine("Player Name,Position,Team,Ownership %,Projected Points");

            foreach (var playerObj in players)
            {
                try
                {
                    var player = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(playerObj));
                    
                    var name = GetPlayerName(player);
                    var position = GetPlayerPosition(player);
                    var team = GetPlayerTeam(player);
                    var ownership = GetOwnershipPercent(player);
                    var projected = GetProjectedPoints(player);

                    csv.AppendLine($"\"{name}\",\"{position}\",\"{team}\",\"{ownership}\",\"{projected}\"");
                }
                catch
                {
                    // Skip malformed player data
                }
            }

            return csv.ToString();
        }

        private string GetPlayerName(JsonElement player)
        {
            if (player.TryGetProperty("player", out var playerInfo))
            {
                if (playerInfo.TryGetProperty("fullName", out var fullName))
                    return fullName.GetString() ?? "Unknown";
            }
            return "Unknown";
        }

        private string GetPlayerPosition(JsonElement player)
        {
            if (player.TryGetProperty("player", out var playerInfo))
            {
                if (playerInfo.TryGetProperty("defaultPositionId", out var posId))
                {
                    return posId.GetInt32() switch
                    {
                        1 => "QB",
                        2 => "RB",
                        3 => "WR",
                        4 => "TE",
                        5 => "K",
                        16 => "DEF",
                        _ => "FLEX"
                    };
                }
            }
            return "FLEX";
        }

        private string GetPlayerTeam(JsonElement player)
        {
            if (player.TryGetProperty("player", out var playerInfo))
            {
                if (playerInfo.TryGetProperty("proTeamId", out var teamId))
                {
                    return teamId.GetInt32() switch
                    {
                        1 => "ATL", 2 => "BUF", 3 => "CHI", 4 => "CIN", 5 => "CLE",
                        6 => "DAL", 7 => "DEN", 8 => "DET", 9 => "GB", 10 => "TEN",
                        11 => "IND", 12 => "KC", 13 => "LV", 14 => "LAR", 15 => "MIA",
                        16 => "MIN", 17 => "NE", 18 => "NO", 19 => "NYG", 20 => "NYJ",
                        21 => "PHI", 22 => "ARI", 23 => "PIT", 24 => "LAC", 25 => "SF",
                        26 => "SEA", 27 => "TB", 28 => "WAS", 29 => "CAR", 30 => "JAX",
                        33 => "BAL", 34 => "HOU",
                        _ => "FA"
                    };
                }
            }
            return "FA";
        }

        private string GetOwnershipPercent(JsonElement player)
        {
            if (player.TryGetProperty("player", out var playerInfo))
            {
                if (playerInfo.TryGetProperty("ownership", out var ownership))
                {
                    if (ownership.TryGetProperty("percentOwned", out var percent))
                        return Math.Round(percent.GetDouble(), 1).ToString();
                }
            }
            return "0.0";
        }

        private string GetProjectedPoints(JsonElement player)
        {
            if (player.TryGetProperty("player", out var playerInfo))
            {
                if (playerInfo.TryGetProperty("stats", out var stats))
                {
                    foreach (var stat in stats.EnumerateArray())
                    {
                        if (stat.TryGetProperty("statSourceId", out var sourceId) && sourceId.GetInt32() == 1)
                        {
                            if (stat.TryGetProperty("appliedTotal", out var total))
                                return Math.Round(total.GetDouble(), 1).ToString();
                        }
                    }
                }
            }
            return "-";
        }
    }
}