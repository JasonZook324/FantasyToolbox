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
            _logger.LogInformation("Gemini API response length: {Length} characters", responseContent.Length);
            
            var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
            
            _logger.LogInformation("Parsed response - Candidates count: {Count}", 
                geminiResponse?.Candidates?.Length ?? 0);
            
            var firstCandidate = geminiResponse?.Candidates?.FirstOrDefault();
            var candidateContent = firstCandidate?.Content;
            var parts = candidateContent?.Parts;
            var text = parts?.FirstOrDefault()?.Text;
            
            _logger.LogInformation("Response parsing - Has candidate: {HasCandidate}, Has content: {HasContent}, Has parts: {HasParts}, Text length: {TextLength}", 
                firstCandidate != null, candidateContent != null, parts != null, text?.Length ?? 0);
            
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger.LogWarning("Gemini returned empty text. Full response: {Response}", responseContent);
                return "Unable to generate recommendations at this time.";
            }
            
            return text;
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

        // Current date and season context
        var currentDate = DateTime.Now;
        var currentSeason = currentDate.Month >= 9 ? currentDate.Year : currentDate.Year - 1;
        sb.AppendLine($"Current Date: {currentDate:MMMM dd, yyyy}");
        sb.AppendLine($"NFL Season: {currentSeason}");
        sb.AppendLine($"Current Week: Week {GetCurrentNFLWeek(currentDate, currentSeason)}");
        sb.AppendLine();
        
        // NFL Schedule context
        sb.AppendLine("CURRENT NFL SCHEDULE CONTEXT:");
        sb.AppendLine(GetNFLScheduleContext(currentDate, currentSeason));
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
            // Include ALL available waiver wire players as requested
            foreach (var player in posGroup.OrderByDescending(p => p.FantasyPoints))
            {
                sb.AppendLine($"  - {player.FullName} ({player.ProTeam}) - {player.FantasyPoints:F1} pts, {player.OwnershipPercentage:F1}% owned, Proj: {player.ProjectedPoints:F1}");
            }
        }
        sb.AppendLine();

        // Analysis request
        sb.AppendLine("ANALYSIS REQUEST:");
        sb.AppendLine("Please provide the following analysis:");
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

    private int GetCurrentNFLWeek(DateTime currentDate, int season)
    {
        // NFL season typically starts first Thursday after Labor Day (first Monday of September)
        var seasonStart = new DateTime(season, 9, 1);
        while (seasonStart.DayOfWeek != DayOfWeek.Monday)
            seasonStart = seasonStart.AddDays(1);
        seasonStart = seasonStart.AddDays(3); // First Thursday after first Monday
        
        if (currentDate < seasonStart)
            return 1; // Pre-season or early season
            
        var daysDiff = (currentDate - seasonStart).Days;
        return Math.Min((daysDiff / 7) + 1, 18); // NFL has 18 weeks (17 regular + playoffs)
    }

    private string GetNFLScheduleContext(DateTime currentDate, int season)
    {
        var sb = new StringBuilder();
        var currentWeek = GetCurrentNFLWeek(currentDate, season);
        
        sb.AppendLine($"- Current NFL Week: {currentWeek}");
        sb.AppendLine($"- Regular Season: Weeks 1-17");
        sb.AppendLine($"- Playoff Period: Weeks 18-22");
        sb.AppendLine();
        
        // Add specific week context
        sb.AppendLine("CURRENT WEEK CONSIDERATIONS:");
        
        // Add bye week information with specific teams when possible
        if (currentWeek >= 4 && currentWeek <= 14)
        {
            sb.AppendLine($"- BYE WEEKS ACTIVE: Teams have bye weeks during weeks 4-14");
            sb.AppendLine($"- Week {currentWeek} bye week implications:");
            sb.AppendLine("  - Check if any roster players have bye weeks this week");
            sb.AppendLine("  - Look for waiver players from non-bye teams");
            sb.AppendLine("  - Consider upcoming bye weeks when selecting pickups");
            
            // Add common bye week schedule patterns
            var byeWeekGuidance = GetByeWeekGuidance(currentWeek);
            if (!string.IsNullOrEmpty(byeWeekGuidance))
            {
                sb.AppendLine(byeWeekGuidance);
            }
        }
        
        // Add playoff context
        if (currentWeek >= 15)
        {
            sb.AppendLine($"- PLAYOFF PUSH (Week {currentWeek}): Focus on players with favorable playoff schedules");
            sb.AppendLine("- Prioritize players on teams still competing for playoffs");
            sb.AppendLine("- Consider rest situations for locked playoff teams");
        }
        
        // Add weekly game context
        sb.AppendLine();
        sb.AppendLine("WEEKLY GAME CONTEXT:");
        sb.AppendLine("- Thursday Night Football: Teams play on short rest (4 days)");
        sb.AppendLine("- Monday Night Football: Teams get extra rest (8 days until next game)");
        sb.AppendLine("- Sunday games: Standard rest (7 days)");
        sb.AppendLine("- Consider travel schedules and divisional matchups");
        
        // Add strength of schedule considerations
        sb.AppendLine();
        sb.AppendLine("MATCHUP ANALYSIS FACTORS:");
        sb.AppendLine("- Target players facing weak defenses in their position");
        sb.AppendLine("- Avoid players facing top-ranked defenses");
        sb.AppendLine("- Consider weather conditions for outdoor games");
        sb.AppendLine("- Look for players in potential high-scoring games");
        sb.AppendLine("- Factor in home field advantage");
        
        return sb.ToString();
    }

    private string GetByeWeekGuidance(int week)
    {
        // Provide general bye week patterns based on typical NFL scheduling
        return week switch
        {
            4 => "  - Early bye weeks: Usually 2-4 teams (often includes Thursday teams from previous week)",
            5 or 6 => "  - Peak bye season beginning: 4-6 teams typically on bye",
            7 or 8 or 9 => "  - Heavy bye weeks: 6 teams commonly on bye (check your roster carefully)",
            10 or 11 => "  - Mid-season byes: 4-6 teams on bye",
            12 or 13 => "  - Late bye weeks: 2-4 teams typically on bye",
            14 => "  - Final bye week: Usually 2 teams on bye",
            _ => ""
        };
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