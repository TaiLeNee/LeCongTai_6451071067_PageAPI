namespace FbApi.BackendApi.Services;

using System.Net.Http.Headers;
using System.Text.Json;

public interface IFacebookActionService
{
    Task<FacebookActionResult> SendAutoReplyAsync(string pageId, string commentId, string message);
    Task<FacebookActionResult> HideCommentAsync(string pageId, string commentId);
}

public record FacebookActionResult(bool Success, string? ResponseData = null, string? ErrorMessage = null);

public class FacebookActionService : IFacebookActionService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FacebookActionService> _logger;

    public FacebookActionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<FacebookActionService> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<FacebookActionResult> SendAutoReplyAsync(string pageId, string commentId, string message)
    {
        var accessToken = _configuration["Facebook:PageAccessToken"];
        var baseUrl = _configuration["Facebook:ApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";

        try
        {
            var canComment = await CanCommentAsync(baseUrl, commentId, accessToken);
            if (canComment == false)
            {
                var errorMessage = "Facebook comment is not commentable for this Page token (can_comment=false).";
                _logger.LogWarning(
                    "Skipping auto reply to comment {CommentId} on page {PageId}: can_comment=false",
                    commentId, pageId);
                return new FacebookActionResult(false, ErrorMessage: errorMessage);
            }

            var url = $"{baseUrl}/{commentId}/comments";
            var payload = new { message };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseData = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"Facebook returned {(int)response.StatusCode} {response.StatusCode}: {responseData}";
                _logger.LogWarning("Facebook auto reply failed for comment {CommentId} on page {PageId}: {StatusCode}",
                    commentId, pageId, response.StatusCode);
                return new FacebookActionResult(false, ErrorMessage: errorMessage);
            }

            return new FacebookActionResult(true, responseData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send auto reply to comment {CommentId} on page {PageId}", commentId, pageId);
            return new FacebookActionResult(false, ErrorMessage: ex.Message);
        }
    }

    private async Task<bool?> CanCommentAsync(string baseUrl, string commentId, string? accessToken)
    {
        var url = $"{baseUrl}/{commentId}?fields=can_comment";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var response = await _httpClient.SendAsync(request);
        var responseData = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Could not check can_comment for comment {CommentId}: {StatusCode}",
                commentId, response.StatusCode);
            return null;
        }

        using var doc = JsonDocument.Parse(responseData);
        if (doc.RootElement.TryGetProperty("can_comment", out var canComment) &&
            canComment.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return canComment.GetBoolean();
        }

        return null;
    }

    public async Task<FacebookActionResult> HideCommentAsync(string pageId, string commentId)
    {
        var accessToken = _configuration["Facebook:PageAccessToken"];
        var baseUrl = _configuration["Facebook:ApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";

        try
        {
            var url = $"{baseUrl}/{commentId}";
            var payload = new { is_hidden = true };

            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            var response = await _httpClient.SendAsync(request);
            var responseData = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                var errorMessage = $"Facebook returned {(int)response.StatusCode} {response.StatusCode}: {responseData}";
                _logger.LogWarning("Facebook hide comment failed for comment {CommentId} on page {PageId}: {StatusCode}",
                    commentId, pageId, response.StatusCode);
                return new FacebookActionResult(false, ErrorMessage: errorMessage);
            }

            return new FacebookActionResult(true, responseData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to hide comment {CommentId} on page {PageId}", commentId, pageId);
            return new FacebookActionResult(false, ErrorMessage: ex.Message);
        }
    }
}
