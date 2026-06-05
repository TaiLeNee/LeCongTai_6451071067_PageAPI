using System.Text.Json;
using FbApi.Contracts.Models;

namespace FbApi.BackendApi.Services;

public enum FacebookErrorClassification
{
    Retryable,
    Permanent
}

public record ClassifiedError(FacebookErrorClassification Classification, string ErrorCode, string Message);

public interface IFacebookApiErrorHandler
{
    ClassifiedError ClassifyError(Exception exception);
}

public class FacebookApiErrorHandler : IFacebookApiErrorHandler
{
    private static readonly HashSet<int> RetryableStatusCodes = new() { 429, 500, 502, 503, 504 };
    private static readonly HashSet<string> RetryableErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "ETIMEDOUT", "RATE_LIMIT", "API_TEMPORARY_UNAVAILABLE", "THROTTLING",
        "REQUEST_TIMEOUT", "SERVER_ERROR", "TEMPORARY_BLOCK"
    };
    private static readonly HashSet<string> PermanentErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "INVALID_TOKEN", "PERMISSION_DENIED", "INVALID_PARAMETER", "OAuthException",
        "190", "200", "10"
    };

    public ClassifiedError ClassifyError(Exception exception)
    {
        if (exception is HttpRequestException httpEx)
        {
            if (httpEx.StatusCode.HasValue && RetryableStatusCodes.Contains((int)httpEx.StatusCode))
                return new ClassifiedError(FacebookErrorClassification.Retryable, ((int)httpEx.StatusCode).ToString(), httpEx.Message);

            if (httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                return new ClassifiedError(FacebookErrorClassification.Permanent, "401", httpEx.Message);

            if (httpEx.StatusCode == System.Net.HttpStatusCode.Forbidden)
                return new ClassifiedError(FacebookErrorClassification.Permanent, "403", httpEx.Message);
        }

        if (exception is TaskCanceledException)
            return new ClassifiedError(FacebookErrorClassification.Retryable, "TIMEOUT", exception.Message);

        var message = exception.Message;
        if (message.Contains("Facebook returned 400", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Facebook returned 401", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("Facebook returned 403", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("can_comment=false", StringComparison.OrdinalIgnoreCase))
        {
            return new ClassifiedError(FacebookErrorClassification.Permanent, "FACEBOOK_PERMANENT_ERROR", message);
        }

        foreach (var code in RetryableErrorCodes)
        {
            if (message.Contains(code, StringComparison.OrdinalIgnoreCase))
                return new ClassifiedError(FacebookErrorClassification.Retryable, code, message);
        }

        foreach (var code in PermanentErrorCodes)
        {
            if (message.Contains(code, StringComparison.OrdinalIgnoreCase))
                return new ClassifiedError(FacebookErrorClassification.Permanent, code, message);
        }

        return new ClassifiedError(FacebookErrorClassification.Retryable, "UNKNOWN", message);
    }
}
