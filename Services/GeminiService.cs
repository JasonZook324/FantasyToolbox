using System.Text;
using System.Text.Json;

namespace FantasyToolbox.Services
{
    public class GeminiService
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<GeminiService> _logger;

        public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
        {
            _httpClient = httpClient;
            _apiKey = configuration["GEMINI_API_KEY"] ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY") ?? "";
            _logger = logger;

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogWarning("GEMINI_API_KEY not found in configuration or environment variables");
            }
        }

        public async Task<string> AnalyzeContextualWaiverWire(object selectedPlayerData, object? waiverWireData, object? nflScheduleData, int topN = 10)
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    return "AI analysis unavailable - API key not configured";
                }

                var selectedPlayerJson = JsonSerializer.Serialize(selectedPlayerData, new JsonSerializerOptions { WriteIndented = true });
                var waiverWireJson = waiverWireData != null ? JsonSerializer.Serialize(waiverWireData, new JsonSerializerOptions { WriteIndented = true }) : "No waiver wire data available";
                var scheduleJson = nflScheduleData != null ? JsonSerializer.Serialize(nflScheduleData, new JsonSerializerOptions { WriteIndented = true }) : "No NFL schedule data available";
                
                var prompt = GenerateContextualWaiverPrompt(selectedPlayerJson, waiverWireJson, scheduleJson, topN);

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
                        temperature = 0.8,
                        topK = 40,
                        topP = 0.95,
                        maxOutputTokens = 2048  // Increased for more detailed analysis
                    },
                    safetySettings = new[]
                    {
                        new
                        {
                            category = "HARM_CATEGORY_HARASSMENT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_HATE_SPEECH", 
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}",
                    content
                );

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
                    
                    return geminiResponse?.candidates?.FirstOrDefault()?.content?.parts?.FirstOrDefault()?.text 
                           ?? "No contextual analysis generated";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error for contextual analysis: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return $"AI contextual analysis temporarily unavailable (Status: {response.StatusCode})";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API for contextual waiver analysis");
                return "AI contextual analysis temporarily unavailable - please try again later";
            }
        }

        public async Task<string> AnalyzePlayerPerformance(object playerData, string analysisType = "general")
        {
            try
            {
                if (string.IsNullOrEmpty(_apiKey))
                {
                    return "AI analysis unavailable - API key not configured";
                }

                var playerJson = JsonSerializer.Serialize(playerData, new JsonSerializerOptions { WriteIndented = true });
                
                var prompt = analysisType switch
                {
                    "waiver_pickup" => GenerateWaiverPickupPrompt(playerJson),
                    "start_sit" => GenerateStartSitPrompt(playerJson),
                    "trade_value" => GenerateTradeValuePrompt(playerJson),
                    _ => GenerateGeneralAnalysisPrompt(playerJson)
                };

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
                        maxOutputTokens = 1024
                    },
                    safetySettings = new[]
                    {
                        new
                        {
                            category = "HARM_CATEGORY_HARASSMENT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_HATE_SPEECH", 
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_SEXUALLY_EXPLICIT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        },
                        new
                        {
                            category = "HARM_CATEGORY_DANGEROUS_CONTENT",
                            threshold = "BLOCK_MEDIUM_AND_ABOVE"
                        }
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync(
                    $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}",
                    content
                );

                if (response.IsSuccessStatusCode)
                {
                    var responseContent = await response.Content.ReadAsStringAsync();
                    var geminiResponse = JsonSerializer.Deserialize<GeminiResponse>(responseContent);
                    
                    return geminiResponse?.candidates?.FirstOrDefault()?.content?.parts?.FirstOrDefault()?.text 
                           ?? "No analysis generated";
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("Gemini API error: {StatusCode} - {Content}", response.StatusCode, errorContent);
                    return $"AI analysis temporarily unavailable (Status: {response.StatusCode})";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling Gemini API for player analysis");
                return "AI analysis temporarily unavailable - please try again later";
            }
        }

        private string GenerateGeneralAnalysisPrompt(string playerJson)
        {
            return $@"As a fantasy football expert, analyze this player's performance and provide insights:

Player Data:
{playerJson}

Please provide a concise analysis (3-4 sentences) covering:
1. Recent performance trends and key statistics
2. Strengths and weaknesses based on the data
3. Overall fantasy value assessment
4. Any relevant injury or team situation context

Keep the analysis practical and actionable for fantasy football decisions.";
        }

        private string GenerateWaiverPickupPrompt(string playerJson)
        {
            return $@"As a fantasy football expert, evaluate this player as a potential waiver wire pickup:

Player Data:
{playerJson}

Please provide a targeted waiver pickup analysis (3-4 sentences) covering:
1. Why this player should or shouldn't be picked up from waivers
2. Realistic expectations for future performance
3. What percentage of FAAB budget or waiver priority this player is worth
4. Best case and worst case scenarios for adding this player

Focus on actionable advice for whether to use a waiver claim on this player.";
        }

        private string GenerateStartSitPrompt(string playerJson)
        {
            return $@"As a fantasy football expert, provide start/sit advice for this player:

Player Data:
{playerJson}

Please provide start/sit guidance (3-4 sentences) covering:
1. Is this player a must-start, situational play, or bench player this week?
2. Key matchup factors and game script considerations
3. Floor and ceiling expectations for this game
4. Alternative players at the same position to consider instead

Give a clear START or SIT recommendation with confidence level (high/medium/low).";
        }

        private string GenerateTradeValuePrompt(string playerJson)
        {
            return $@"As a fantasy football expert, assess this player's trade value:

Player Data:
{playerJson}

Please provide a trade value analysis (3-4 sentences) covering:
1. Current market value - is this player buy-low, sell-high, or fairly valued?
2. ROS (rest of season) outlook and potential for improvement/decline
3. What tier of players this player could realistically be traded for
4. Timing considerations - best time to trade this player

Focus on practical trade advice and realistic player comparisons for trade targets.";
        }

        private string GenerateContextualWaiverPrompt(string selectedPlayerJson, string waiverWireJson, string nflScheduleJson, int topN)
        {
            return $@"As a fantasy football expert, provide contextual waiver wire recommendations based on this specific roster situation.

SELECTED ROSTER PLAYER:
{selectedPlayerJson}

AVAILABLE WAIVER WIRE PLAYERS:
{waiverWireJson}

CURRENT NFL SCHEDULE/MATCHUPS:
{nflScheduleJson}

ANALYSIS REQUEST:
Provide strategic waiver wire recommendations for the top {topN} players based on the selected roster player. Consider:

1. **ROSTER CONTEXT**: How do the available waiver players compare to/complement the selected roster player?
2. **POSITIONAL STRATEGY**: 
   - If same position: Who offers better upside, consistency, or matchup advantages?
   - If different position: Which players provide the best roster balance and flexibility?
3. **STREAMING OPPORTUNITIES**: Based on current NFL schedule data:
   - Which players have favorable upcoming matchups?
   - Any streaming plays for QB/K/D/ST positions?
   - Week-to-week scheduling advantages?
4. **PRACTICAL RECOMMENDATIONS**:
   - Priority ranking of top waiver targets (1-{topN})
   - Suggested FAAB percentages or waiver priority usage
   - Drop candidates from current roster if needed
   - Short-term vs. long-term value propositions

5. **MATCHUP INTELLIGENCE**: Use the NFL schedule data to identify:
   - Players with easier upcoming matchups
   - Teams on bye weeks to avoid
   - Favorable game scripts (projected high-scoring games)
   - Weather/venue considerations if available

FORMAT YOUR RESPONSE:
Start with a brief summary of the strategic situation, then provide numbered recommendations for each top waiver target with specific reasoning, FAAB suggestions, and timing advice. Include any relevant streaming or schedule-based insights.

Focus on actionable, data-driven advice that considers both immediate needs and longer-term roster construction.";
        }
    }

    // Response models for Gemini API
    public class GeminiResponse
    {
        public GeminiCandidate[]? candidates { get; set; }
    }

    public class GeminiCandidate
    {
        public GeminiContent? content { get; set; }
    }

    public class GeminiContent
    {
        public GeminiPart[]? parts { get; set; }
    }

    public class GeminiPart
    {
        public string? text { get; set; }
    }
}