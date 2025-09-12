using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FantasyToolbox.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class PlayersController : ControllerBase
    {
        private readonly ApplicationDbContext _context;

        public PlayersController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpGet("{sport}/{season}")]
        public async Task<IActionResult> GetPlayers(string sport, string season, [FromQuery] string userId = "default-user")
        {
            try
            {
                using var httpClient = new HttpClient();
                
                // ESPN Global Players API - this gets the full player database (hundreds of players)
                var apiUrl = $"https://lm-api-reads.fantasy.espn.com/apis/v3/sports/football/nfl/seasons/{season}/players";
                
                var allPlayers = new List<object>();
                int pageSize = 50;
                int offset = 0;
                bool hasMoreData = true;

                // Paginate through all players
                while (hasMoreData && allPlayers.Count < 500) // Limit to 500 to prevent endless loops
                {
                    var paginatedUrl = $"{apiUrl}?limit={pageSize}&offset={offset}";
                    
                    var response = await httpClient.GetAsync(paginatedUrl);
                    if (!response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    var jsonContent = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(jsonContent);
                    
                    if (doc.RootElement.TryGetProperty("players", out var playersArray))
                    {
                        var pagePlayers = playersArray.EnumerateArray().ToList();
                        if (pagePlayers.Count == 0)
                        {
                            hasMoreData = false;
                        }
                        else
                        {
                            allPlayers.AddRange(pagePlayers.Select(p => JsonSerializer.Deserialize<object>(p.GetRawText())));
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
                    season = season,
                    sport = sport
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = $"Failed to fetch players: {ex.Message}" });
            }
        }
    }
}