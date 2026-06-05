using FbApi.Contracts.Models;
using FbApi.CoreService.Services;
using FluentAssertions;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class SpamDetectorServiceTests
{
    private readonly SpamDetectorService _sut = new();

    private static NormalizedEvent MakeEvent(string message) => new()
    {
        EventId = Guid.NewGuid().ToString(),
        CorrelationId = Guid.NewGuid().ToString(),
        EventType = "comment",
        Source = "facebook",
        PageId = "page123",
        PostId = "post123",
        CommentId = "comment123",
        UserId = "user123",
        UserName = "TestUser",
        Message = message
    };

    [Fact]
    public void HttpLink_DetectedAsLinkSpam()
    {
        var result = _sut.Detect(MakeEvent("check this http://example.com"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("link");
    }

    [Fact]
    public void HttpsLink_DetectedAsLinkSpam()
    {
        var result = _sut.Detect(MakeEvent("visit https://example.com now"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("link");
    }

    [Fact]
    public void MessageWithLink_IsSpam_WithSpamTypeLink()
    {
        var result = _sut.Detect(MakeEvent("check this out http://spam.com"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("link");
    }

    [Fact]
    public void RepeatedContent_DetectedAsRepeatedSpam()
    {
        var result = _sut.Detect(MakeEvent("buy now buy now buy now buy now"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("repeated");
    }

    [Fact]
    public void ScamKeywords_DetectedAsScamSpam()
    {
        var scamMessages = new[]
        {
            "Get free money today!",
            "Click here to win",
            "You are the winner of our lottery",
            "Congratulations you won a prize",
            "Best crypto investment opportunity",
            "Subscribe now for amazing deals"
        };

        foreach (var msg in scamMessages)
        {
            var result = _sut.Detect(MakeEvent(msg));
            result.IsSpam.Should().BeTrue($"message '{msg}' should be detected as scam");
            result.SpamType.Should().Be("scam");
        }
    }

    [Fact]
    public void NormalMessage_NotDetectedAsSpam()
    {
        var result = _sut.Detect(MakeEvent("Hello, how much is this?"));

        result.IsSpam.Should().BeFalse();
        result.SpamType.Should().Be("none");
    }

    [Fact]
    public void MessageWithOnlyUrl_IsLinkSpam()
    {
        var result = _sut.Detect(MakeEvent("http://only-url.com"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("link");
    }

    [Fact]
    public void MessageWithScamKeywordButNoUrl_IsScamSpam()
    {
        var result = _sut.Detect(MakeEvent("Get your free money right now"));

        result.IsSpam.Should().BeTrue();
        result.SpamType.Should().Be("scam");
    }

    [Fact]
    public void EmptyMessage_NotSpam()
    {
        var result = _sut.Detect(MakeEvent(""));

        result.IsSpam.Should().BeFalse();
        result.SpamType.Should().Be("none");
    }
}
