using Microsoft.AspNetCore.Mvc;

namespace FbApi.BackendApi.Controllers;

[ApiController]
[Route("comments")]
public class CommentsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CommentsController> _logger;

    public CommentsController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<CommentsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public Task<IActionResult> GetCommentsByQuery([FromQuery] string postId)
    {
        return FetchCommentsAsync(postId);
    }

    [HttpGet("{postId}")]
    public Task<IActionResult> GetComments(string postId)
    {
        return FetchCommentsAsync(postId);
    }

    private async Task<IActionResult> FetchCommentsAsync(string postId)
    {
        if (string.IsNullOrWhiteSpace(postId))
        {
            return BadRequest(new
            {
                success = false,
                error = new { code = "POST_ID_REQUIRED", message = "postId is required." }
            });
        }

        var accessToken = _configuration["Facebook:PageAccessToken"];
        var baseUrl = _configuration["Facebook:ApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";

        var url = $"{baseUrl}/{postId}/comments?access_token={accessToken}";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("FacebookApi");
            var response = await httpClient.GetAsync(url);
            _logger.LogInformation("Facebook GET comments returned {StatusCode} for post {PostId}", response.StatusCode, postId);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return Ok(new { success = true, data = content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch comments for post {PostId}", postId);
            return StatusCode(502, new
            {
                success = false,
                error = new { code = "FACEBOOK_COMMENTS_FETCH_FAILED", message = "Failed to fetch comments from Facebook" }
            });
        }
    }
}
