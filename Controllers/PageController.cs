using Microsoft.AspNetCore.Mvc;
using System.Net.Http;

namespace LeCongTai_6451071067_PageAPI.Controllers 
{
    [ApiController]
    [Route("api/page")]
    public class PageController : ControllerBase
    {
        private readonly HttpClient _httpClient;
        private readonly string _accessToken;
        private readonly string _baseUrl;

        public PageController(IHttpClientFactory httpClientFactory, IConfiguration config)
        {
            _httpClient = httpClientFactory.CreateClient();
            _baseUrl = config["FacebookApi:BaseUrl"];
            _accessToken = config["FacebookApi:PageAccessToken"];
            _httpClient.BaseAddress = new Uri(_baseUrl);
        }

        [HttpGet("{pageId}")]
        public async Task<IActionResult> GetPage(string pageId)
        {
            var response = await _httpClient.GetAsync($"{pageId}?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpGet("{pageId}/posts")]
        public async Task<IActionResult> GetPosts(string pageId)
        {
            var response = await _httpClient.GetAsync($"{pageId}/posts?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpPost("{pageId}/posts")]
        public async Task<IActionResult> CreatePost(string pageId, [FromBody] PostModel model)
        {
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("message", model.Message),
                new KeyValuePair<string, string>("access_token", _accessToken)
            });

            var response = await _httpClient.PostAsync($"{pageId}/feed", content);
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpDelete("post/{postId}")]
        public async Task<IActionResult> DeletePost(string postId)
        {
            var response = await _httpClient.DeleteAsync($"{postId}?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpGet("post/{postId}/comments")]
        public async Task<IActionResult> GetComments(string postId)
        {
            var response = await _httpClient.GetAsync($"{postId}/comments?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpGet("post/{postId}/likes")]
        public async Task<IActionResult> GetLikes(string postId)
        {
            var response = await _httpClient.GetAsync($"{postId}/likes?access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }

        [HttpGet("{pageId}/insights")]
        public async Task<IActionResult> GetInsights(string pageId, [FromQuery] string metric = "page_impressions", [FromQuery] string period = "day")
        {
            var response = await _httpClient.GetAsync($"{pageId}/insights?metric={metric}&period={period}&access_token={_accessToken}");
            return Ok(await response.Content.ReadAsStringAsync());
        }
    }
    public class PostModel
    {
        public string Message { get; set; } = string.Empty;
    }
}