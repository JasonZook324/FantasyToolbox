using System.Text.Json;
using System.Text;

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
                    ]
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
1. Current performance trends and key stats
2. Upcoming matchup outlook  
3. Fantasy relevance and roster decision guidance
4. Any injury concerns or red flags

Focus on actionable fantasy advice for the upcoming week.";
        }

        private string GenerateWaiverPickupPrompt(string playerJson)
        {
            return $@"As a fantasy football expert, evaluate this player as a potential waiver wire pickup:

Player Data:
{playerJson}

Provide a brief waiver priority assessment (2-3 sentences) covering:
1. Why this player is worth considering for pickup
2. Expected role and opportunity moving forward
3. Recommended FAAB percentage or waiver priority level
4. Best roster situations where this pickup makes sense

Be specific about their upside and realistic expectations.";
        }

        private string GenerateStartSitPrompt(string playerJson)
        {
            return $@"As a fantasy football expert, provide start/sit advice for this player:

Player Data:
{playerJson}

Give a clear recommendation (2-3 sentences) covering:
1. Start or Sit recommendation with confidence level
2. Key matchup factors influencing the decision
3. Floor vs ceiling expectations for this week
4. Alternative options to consider if available

Focus on this week's specific outlook and decision factors.";
        }

        private string GenerateTradeValuePrompt(string playerJson)
        {
            return $@"As a fantasy football expert, assess this player's trade value:

Player Data:
{playerJson}

Provide a trade value analysis (3-4 sentences) covering:
1. Current trade market value and tier ranking
2. Buy-low or sell-high opportunity assessment  
3. Realistic trade targets of similar value
4. ROS outlook impact on value trajectory

Focus on actionable trade guidance and market positioning.";
        }
    }

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