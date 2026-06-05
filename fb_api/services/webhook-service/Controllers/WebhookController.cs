using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using FbApi.WebhookService.Kafka;
using FbApi.WebhookService.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace FbApi.WebhookService.Controllers;

[ApiController]
[Route("webhook")]
public class WebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly HmacValidationService _hmac;
    private readonly FacebookPayloadParser _parser;
    private readonly NormalizedEventMapper _mapper;
    private readonly RawEventsProducer _producer;
    private readonly ILogger<WebhookController> _logger;

    public WebhookController(
        IConfiguration config,
        HmacValidationService hmac,
        FacebookPayloadParser parser,
        NormalizedEventMapper mapper,
        RawEventsProducer producer,
        ILogger<WebhookController> logger)
    {
        _config = config;
        _hmac = hmac;
        _parser = parser;
        _mapper = mapper;
        _producer = producer;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge)
    {
        if (mode != "subscribe")
            return BadRequest("Invalid hub.mode");

        var expectedToken = _config["Facebook:VerifyToken"] ?? _config["Facebook__VerifyToken"];
        if (string.IsNullOrWhiteSpace(expectedToken))
        {
            _logger.LogError("Facebook verify token is not configured");
            return StatusCode(503, "Verify token is not configured");
        }

        if (verifyToken != expectedToken)
        {
            _logger.LogWarning("Webhook verification failed: invalid verify token");
            return Unauthorized("Invalid verify_token");
        }

        _logger.LogInformation("Webhook verified successfully");
        return Ok(challenge ?? string.Empty);
    }

    [HttpPost]
    public async Task<IActionResult> Receive()
    {
        string rawPayload;
        using (var reader = new StreamReader(Request.Body, Encoding.UTF8))
        {
            rawPayload = await reader.ReadToEndAsync();
        }

        // Validate HMAC signature
        Request.Headers.TryGetValue("X-Hub-Signature-256", out var signatureHeader);
        var appSecret = _config["Facebook:AppSecret"] ?? _config["Facebook__AppSecret"] ?? string.Empty;

        if (!_hmac.Validate(rawPayload, signatureHeader.ToString(), appSecret))
        {
            _logger.LogWarning("Invalid HMAC signature received");
            return Unauthorized("Invalid signature");
        }

        // Parse FB payload
        var changes = _parser.Parse(rawPayload);

        if (changes.Count == 0)
        {
            _logger.LogInformation("No relevant changes in webhook payload");
            return Ok("ok");
        }

        // Map and produce to Kafka
        foreach (var change in changes)
        {
            if (change.Field != "feed" ||
                change.Item != "comment" ||
                change.Verb != "add" ||
                string.IsNullOrWhiteSpace(change.CommentId) ||
                string.IsNullOrWhiteSpace(change.Message))
            {
                _logger.LogInformation(
                    "Skipping non-comment webhook change field={Field} item={Item} verb={Verb}",
                    change.Field, change.Item, change.Verb);
                continue;
            }

            if (string.Equals(change.FromId, change.PageId, StringComparison.Ordinal))
            {
                _logger.LogInformation(
                    "Skipping page-authored comment {CommentId} to avoid auto-reply loops",
                    change.CommentId);
                continue;
            }

            var normalized = _mapper.Map(change, rawPayload);
            await _producer.ProduceAsync(normalized);
        }

        _logger.LogInformation("Processed {Count} webhook changes", changes.Count);

        // Return 200 fast — Facebook expects quick response
        return Ok("ok");
    }
}
