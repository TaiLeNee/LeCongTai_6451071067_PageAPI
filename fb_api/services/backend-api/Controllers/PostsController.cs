using FbApi.BackendApi.Filters;
using Microsoft.AspNetCore.Mvc;

namespace FbApi.BackendApi.Controllers;

[ApiController]
[Route("posts")]
public class PostsController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PostsController> _logger;

    public PostsController(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<PostsController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetPosts([FromQuery] string? pageId = null)
    {
        var accessToken = _configuration["Facebook:PageAccessToken"];
        var baseUrl = _configuration["Facebook:ApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";

        var targetId = string.IsNullOrWhiteSpace(pageId) ? "me" : pageId;
        var url = $"{baseUrl}/{targetId}/posts?fields=id,message,created_time,permalink_url&access_token={accessToken}";

        try
        {
            var httpClient = _httpClientFactory.CreateClient("FacebookApi");
            var response = await httpClient.GetAsync(url);
            _logger.LogInformation("Facebook GET posts returned {StatusCode} for target {TargetId}", response.StatusCode, targetId);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return Ok(new { success = true, data = content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch posts from Facebook");
            return StatusCode(502, new
            {
                success = false,
                error = new { code = "FACEBOOK_POSTS_FETCH_FAILED", message = "Failed to fetch posts from Facebook" }
            });
        }
    }

    [HttpPost]
    [HttpPost("/post")]
    [AdminApiKey]
    public async Task<IActionResult> CreatePost([FromBody] CreatePostRequest request)
    {
        var accessToken = _configuration["Facebook:PageAccessToken"];
        var baseUrl = _configuration["Facebook:ApiBaseUrl"] ?? "https://graph.facebook.com/v19.0";

        var url = $"{baseUrl}/{request.PageId}/feed?access_token={accessToken}";
        var payload = new { message = request.Message };

        try
        {
            var httpClient = _httpClientFactory.CreateClient("FacebookApi");
            var response = await httpClient.PostAsJsonAsync(url, payload);
            _logger.LogInformation("Facebook create post returned {StatusCode} for page {PageId}", response.StatusCode, request.PageId);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            return Ok(new { success = true, data = content });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create post on page {PageId}", request.PageId);
            return StatusCode(502, new
            {
                success = false,
                error = new { code = "FACEBOOK_POST_CREATE_FAILED", message = "Failed to create post on Facebook" }
            });
        }
    }
}

public record CreatePostRequest(string PageId, string Message);
