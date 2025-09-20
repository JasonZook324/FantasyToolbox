using Microsoft.AspNetCore.Mvc;
using FantasyToolbox.Services;
using FantasyToolbox.Pages;

namespace FantasyToolbox.Controllers;

[Route("api/[controller]")]
[ApiController]
public class RecommendationsController : ControllerBase
{
    private readonly GeminiRecommendationService _geminiService;
    private readonly IUserService _userService;
    private readonly ILogger<RecommendationsController> _logger;

    public RecommendationsController(
        GeminiRecommendationService geminiService,
        IUserService userService,
        ILogger<RecommendationsController> logger)
    {
        _geminiService = geminiService;
        _userService = userService;
        _logger = logger;
    }

    [HttpPost("waiver-wire")]
    public async Task<IActionResult> GetWaiverWireRecommendations([FromBody] WaiverRecommendationRequest request)
    {
        try
        {
            // Get the user's email from the session or request
            var userEmail = HttpContext.Session.GetString("UserEmail");
            if (string.IsNullOrEmpty(userEmail))
            {
                return Unauthorized(new { error = "User not authenticated" });
            }

            _logger.LogInformation("Getting waiver wire recommendations for user: {UserEmail}", userEmail);

            // Get user data
            var user = await _userService.GetUserByEmailAsync(userEmail);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (request.Roster == null || !request.Roster.Any())
            {
                return BadRequest(new { error = "Roster data is required" });
            }

            if (request.WaiverPlayers == null || !request.WaiverPlayers.Any())
            {
                return BadRequest(new { error = "Waiver wire players data is required" });
            }

            // Get AI recommendations
            var recommendations = await _geminiService.GetWaiverWireRecommendationsAsync(
                request.Roster, 
                request.WaiverPlayers, 
                request.PositionFilter);

            return Ok(new WaiverRecommendationResponse
            {
                Recommendations = recommendations,
                UserEmail = userEmail,
                PositionFilter = request.PositionFilter,
                GeneratedAt = DateTime.UtcNow,
                RosterCount = request.Roster.Count,
                WaiverPlayersCount = request.WaiverPlayers.Count
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting waiver wire recommendations");
            return StatusCode(500, new { error = "Failed to generate recommendations. Please try again later." });
        }
    }

    // Request/Response models
    public class WaiverRecommendationRequest
    {
        public List<WaiverWireModel.RosterPlayer> Roster { get; set; } = new();
        public List<WaiverWireModel.WaiverWirePlayer> WaiverPlayers { get; set; } = new();
        public string? PositionFilter { get; set; }
    }

    public class WaiverRecommendationResponse
    {
        public string Recommendations { get; set; } = "";
        public string UserEmail { get; set; } = "";
        public string? PositionFilter { get; set; }
        public DateTime GeneratedAt { get; set; }
        public int RosterCount { get; set; }
        public int WaiverPlayersCount { get; set; }
    }
}