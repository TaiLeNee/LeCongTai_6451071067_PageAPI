using System.Text;
using System.Text.Json;
using System.Globalization;
using FbApi.Contracts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace FbApi.CoreService.Services;

public interface IGeminiService
{
    Task<AiAnalysisResult> AnalyzeAsync(NormalizedEvent evt);
}

public class GeminiService : IGeminiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeminiService> _logger;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly bool _apiKeyMissing;
    private readonly ResiliencePipeline<HttpResponseMessage> _resiliencePipeline;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public GeminiService(HttpClient httpClient, IConfiguration configuration, ILogger<GeminiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/");

        _apiKey = configuration["Gemini:ApiKey"] ?? configuration["Gemini__ApiKey"];
        _model = configuration["Gemini:Model"] ?? configuration["Gemini__Model"] ?? "gemini-2.0-flash";
        if (string.IsNullOrEmpty(_apiKey))
        {
            _apiKeyMissing = true;
            _logger.LogWarning("Gemini:ApiKey not configured — AI features will be unavailable");
        }
        _httpClient.DefaultRequestHeaders.Clear();

        _resiliencePipeline = new ResiliencePipelineBuilder<HttpResponseMessage>()
            .AddRetry(new RetryStrategyOptions<HttpResponseMessage>
            {
                MaxRetryAttempts = 3,
                Delay = TimeSpan.FromMilliseconds(500),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().HandleResult(r => !r.IsSuccessStatusCode),
                OnRetry = args =>
                {
                    _logger.LogWarning("Gemini API retry attempt {Attempt}", args.AttemptNumber);
                    return default;
                }
            })
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage>
            {
                FailureRatio = 0.5,
                MinimumThroughput = 10,
                SamplingDuration = TimeSpan.FromSeconds(30),
                BreakDuration = TimeSpan.FromSeconds(30),
                ShouldHandle = new PredicateBuilder<HttpResponseMessage>().Handle<HttpRequestException>().HandleResult(r => !r.IsSuccessStatusCode),
                OnOpened = args =>
                {
                    _logger.LogWarning("Circuit breaker OPENED for {Duration}s", args.BreakDuration.TotalSeconds);
                    return default;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Circuit breaker CLOSED - resumed normal flow");
                    return default;
                }
            })
            .Build();
    }

    public async Task<AiAnalysisResult> AnalyzeAsync(NormalizedEvent evt)
    {
        if (_apiKeyMissing)
        {
            _logger.LogWarning("Gemini API key missing — returning manual_review fallback");
            return FallbackResult(evt.Message, "ai_unavailable (Gemini API key not configured)");
        }

        try
        {
            var result = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var prompt = BuildPrompt(evt.Message);
                var requestBody = new
                {
                    contents = new[]
                    {
                        new { parts = new[] { new { text = prompt } } }
                    },
                    generationConfig = new
                    {
                        responseMimeType = "application/json"
                    }
                };

                var json = JsonSerializer.Serialize(requestBody);
                using var request = new HttpRequestMessage(HttpMethod.Post, $"models/{_model}:generateContent")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);

                var response = await _httpClient.SendAsync(request, ct);

                return response;
            });

            if (!result.IsSuccessStatusCode)
            {
                var errorBody = await result.Content.ReadAsStringAsync();
                _logger.LogError("Gemini API returned {StatusCode}: {ErrorBody}",
                    result.StatusCode, Truncate(errorBody, 500));
                return FallbackResult(evt.Message, $"gemini_http_{(int)result.StatusCode}");
            }

            var responseBody = await result.Content.ReadAsStringAsync();
            return ParseGeminiResponse(responseBody, evt.Message);
        }
        catch (BrokenCircuitException)
        {
            _logger.LogWarning("Circuit breaker is open - returning fallback");
            return FallbackResult(evt.Message, "gemini_circuit_open");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini analysis failed");
            return FallbackResult(evt.Message, "gemini_exception");
        }
    }

    private static string BuildPrompt(string message)
    {
        return $@"Analyze this Facebook comment for intent and sentiment. Return ONLY valid JSON.

Comment: ""{message}""

Return JSON with exactly these fields:
{{""intent"": ""<complaint|price_inquiry|positive|neutral|question|spam>"", ""sentiment"": ""<positive|negative|neutral>"", ""confidence"": <0.0-1.0>, ""reason"": ""<brief explanation>""}}";
    }

    private AiAnalysisResult ParseGeminiResponse(string responseBody, string originalMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var text = doc.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString()!;

            var analysis = JsonSerializer.Deserialize<GeminiAnalysis>(NormalizeJsonText(text), JsonOptions);
            if (analysis == null)
                return FallbackResult(originalMessage, "gemini_empty_analysis");

            return new AiAnalysisResult
            {
                Intent = analysis.Intent ?? "neutral",
                Sentiment = analysis.Sentiment ?? "neutral",
                Confidence = analysis.Confidence,
                AnalyzedAt = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse Gemini response, using fallback");
            return FallbackResult(originalMessage, "gemini_parse_failed");
        }
    }

    private static AiAnalysisResult FallbackResult(string message = "", string reason = "gemini_unavailable")
    {
        var normalized = NormalizeForMatching(message);

        if (ContainsAny(normalized, "gia", "bao nhieu", "bao gia", "price", "cost"))
        {
            return new AiAnalysisResult
            {
                Intent = "price_inquiry",
                Sentiment = "neutral",
                Confidence = 0.85,
                AnalyzedAt = DateTime.UtcNow,
                Reason = $"{reason}; local_fallback"
            };
        }

        if (ContainsAny(normalized, "cam on", "tot", "thich", "hay", "dep", "good", "ok"))
        {
            return new AiAnalysisResult
            {
                Intent = "positive",
                Sentiment = "positive",
                Confidence = 0.8,
                AnalyzedAt = DateTime.UtcNow,
                Reason = $"{reason}; local_fallback"
            };
        }

        if (ContainsAny(normalized, "khong hai long", "that vong", "loi", "te", "bad", "complaint"))
        {
            return new AiAnalysisResult
            {
                Intent = "complaint",
                Sentiment = "negative",
                Confidence = 0.8,
                AnalyzedAt = DateTime.UtcNow,
                Reason = $"{reason}; local_fallback"
            };
        }

        if (message.Contains('?') || ContainsAny(normalized, "khong", "co", "can", "hoi", "question"))
        {
            return new AiAnalysisResult
            {
                Intent = "question",
                Sentiment = "neutral",
                Confidence = 0.75,
                AnalyzedAt = DateTime.UtcNow,
                Reason = $"{reason}; local_fallback"
            };
        }

        return new AiAnalysisResult
        {
            Intent = "manual_review",
            Sentiment = "neutral",
            Confidence = 0.0,
            AnalyzedAt = DateTime.UtcNow,
            Reason = reason
        };
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        return terms.Any(value.Contains);
    }

    private static string NormalizeForMatching(string value)
    {
        var normalized = value.ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(capacity: normalized.Length);

        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder
            .ToString()
            .Replace('đ', 'd')
            .Normalize(NormalizationForm.FormC);
    }

    private static string NormalizeJsonText(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            return trimmed;

        var firstNewLine = trimmed.IndexOf('\n');
        if (firstNewLine < 0)
            return trimmed;

        var withoutOpeningFence = trimmed[(firstNewLine + 1)..];
        var closingFence = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
        return closingFence >= 0
            ? withoutOpeningFence[..closingFence].Trim()
            : withoutOpeningFence.Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;

        return value[..maxLength] + "...";
    }

    private class GeminiAnalysis
    {
        public string? Intent { get; set; }
        public string? Sentiment { get; set; }
        public double Confidence { get; set; }
        public string? Reason { get; set; }
    }
}
