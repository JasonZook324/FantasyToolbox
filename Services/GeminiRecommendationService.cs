using System.Text;
using System.Text.Json;
using FantasyToolbox.Pages;

namespace FantasyToolbox.Services;

public class GeminiRecommendationService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly ILogger<GeminiRecommendationService> _logger;

    public GeminiRecommendationService(HttpClient httpClient, ILogger<GeminiRecommendationService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? throw new InvalidOperationException("GEMINI_API_KEY environment variable is required");
    }

    public async Task<string> GetWaiverWireRecommendationsAsync(
        List<WaiverWireModel.RosterPlayer> roster,
        List<WaiverWireModel.WaiverWirePlayer> waiverPlayers,
        string? positionFilter = null)
    {
        try
        {
            var prompt = BuildWaiverWirePrompt(roster, waiverPlayers, positionFilter);
            
            var requestBody = new
            {
                contents = new[]
                {
                    new
                    {
                        parts = new[]
                        {
                            new { text = prompt }
                        }
                    }
                },
                generationConfig = new
                {
                    temperature = 0.7,
                    topK = 40,
                    topP = 0.95,
                    maxOutputTokens = 2048,
                }
            };

            var jsonContent = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(
                $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}",
                content);

            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
                throw new HttpRequestException($"Gemini API returned {response.StatusCode}: {response.ReasonPhrase}");
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
            
            return geminiResponse?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text 
                   ?? "Unable to generate recommendations at this time.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting waiver wire recommendations");
            return "Sorry, I'm unable to provide recommendations right now. Please try again later.";
        }
    }

    private string BuildWaiverWirePrompt(
        List<WaiverWireModel.RosterPlayer> roster,
        List<WaiverWireModel.WaiverWirePlayer> waiverPlayers,
        string? positionFilter)
    {
        var sb = new StringBuilder();
        
        sb.AppendLine("You are an expert fantasy football analyst. Analyze the following data and recommend the best waiver wire pickups.");
        sb.AppendLine();

        // Current date context
        sb.AppendLine($"Current Date: {DateTime.Now:MMMM dd, yyyy}");
        sb.AppendLine($"NFL Season: 2024");
        sb.AppendLine();

        // Roster analysis
        sb.AppendLine("CURRENT ROSTER:");
        var rosterByPosition = roster.GroupBy(p => p.Position).OrderBy(g => g.Key);
        foreach (var posGroup in rosterByPosition)
        {
            sb.AppendLine($"{posGroup.Key}: {string.Join(", ", posGroup.Select(p => $"{p.FullName} ({p.ProTeam})"))}");
        }
        sb.AppendLine();

        // Available players
        sb.AppendLine("AVAILABLE WAIVER WIRE PLAYERS:");
        if (!string.IsNullOrEmpty(positionFilter))
        {
            sb.AppendLine($"(Filtered to {positionFilter} position)");
        }

        var waiverByPosition = waiverPlayers.GroupBy(p => p.Position).OrderBy(g => g.Key);
        foreach (var posGroup in waiverByPosition)
        {
            sb.AppendLine($"\n{posGroup.Key}:");
            foreach (var player in posGroup.Take(10)) // Limit to top 10 per position
            {
                sb.AppendLine($"  • {player.FullName} ({player.ProTeam}) - {player.FantasyPoints:F1} pts, {player.OwnershipPercentage:F1}% owned, Proj: {player.ProjectedPoints:F1}");
            }
        }
        sb.AppendLine();

        // Analysis request
        sb.AppendLine("ANALYSIS REQUEST:");
        sb.AppendLine("Please provide:")
        sb.AppendLine("1. TOP 3 RECOMMENDED PICKUPS with specific reasons why each player would improve this roster");
        sb.AppendLine("2. POSITION ANALYSIS highlighting any weak spots in the current roster");
        sb.AppendLine("3. STRATEGIC CONSIDERATIONS for upcoming weeks based on player schedules and trends");
        sb.AppendLine();
        sb.AppendLine("Consider factors like:");
        sb.AppendLine("- Roster depth and bye week coverage");
        sb.AppendLine("- Player opportunity and recent performance trends");
        sb.AppendLine("- Injury situations affecting playing time");
        sb.AppendLine("- Schedule difficulty and upcoming matchups");
        sb.AppendLine("- Ownership percentages indicating hidden gems");
        sb.AppendLine();
        sb.AppendLine("Format your response clearly with headers and bullet points for easy reading.");

        return sb.ToString();
    }

    // Response models for JSON deserialization
    public class GeminiResponse
    {
        public GeminiCandidate[]? Candidates { get; set; }
    }

    public class GeminiCandidate
    {
        public GeminiContent? Content { get; set; }
    }

    public class GeminiContent
    {
        public GeminiPart[]? Parts { get; set; }
    }

    public class GeminiPart
    {
        public string? Text { get; set; }
    }
}