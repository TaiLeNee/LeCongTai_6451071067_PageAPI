using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using FbApi.CoreService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class RuleEngineServiceTests
{
    private readonly Mock<ISpamTrackerService> _spamTrackerMock;
    private readonly Mock<ILogger<RuleEngineService>> _loggerMock;
    private readonly RuleEngineService _sut;

    public RuleEngineServiceTests()
    {
        _spamTrackerMock = new Mock<ISpamTrackerService>();
        _loggerMock = new Mock<ILogger<RuleEngineService>>();
        _sut = new RuleEngineService(_spamTrackerMock.Object, _loggerMock.Object);
    }

    private static NormalizedEvent MakeEvent(string userId = "user123", string message = "hello") => new()
    {
        EventId = Guid.NewGuid().ToString(),
        CorrelationId = Guid.NewGuid().ToString(),
        EventType = "comment",
        Source = "facebook",
        PageId = "page123",
        PostId = "post123",
        CommentId = "comment123",
        UserId = userId,
        UserName = "TestUser",
        Message = message
    };

    private static AiAnalysisResult MakeAi(string intent = "neutral", string sentiment = "neutral", double confidence = 0.8) => new()
    {
        Intent = intent,
        Sentiment = sentiment,
        Confidence = confidence
    };

    [Fact]
    public void BlacklistedUser_ActionIsBlacklistUser_AndShouldHide_AndRequiresManualReview()
    {
        _spamTrackerMock.Setup(x => x.IsBlacklisted("baduser")).Returns(true);
        var result = _sut.Evaluate(MakeEvent("baduser"), new SpamDetectorResult { IsSpam = false, SpamType = "none" }, MakeAi());

        result.Action.Should().Be(ActionTypes.BlacklistUser);
        result.InternalBlacklist.Should().BeTrue();
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void MaliciousScamSpam_HideAndReview_RequiresManualReview()
    {
        var spamResult = new SpamDetectorResult { IsSpam = true, SpamType = "scam" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi());

        result.Action.Should().Be(ActionTypes.HideComment);
        result.ShouldHide.Should().BeTrue();
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void RepeatedSpammer3x_ActionIsBlacklistUser_NoAutoReply()
    {
        _spamTrackerMock.Setup(x => x.IsRepeatedSpammer("spammer")).Returns(true);
        var spamResult = new SpamDetectorResult { IsSpam = true, SpamType = "link" };

        var result = _sut.Evaluate(MakeEvent("spammer"), spamResult, MakeAi());

        result.Action.Should().Be(ActionTypes.BlacklistUser);
        result.ShouldHide.Should().BeTrue();
        result.InternalBlacklist.Should().BeTrue();
        result.ReplyText.Should().BeEmpty("repeated spammers should not get auto-reply");
        _spamTrackerMock.Verify(x => x.BlacklistUser("spammer"), Times.Once);
    }

    [Fact]
    public void NormalSpam_Link_ActionIsAutoHide_ShouldHide()
    {
        var spamResult = new SpamDetectorResult { IsSpam = true, SpamType = "link" };
        _spamTrackerMock.Setup(x => x.IsRepeatedSpammer(It.IsAny<string>())).Returns(false);

        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi());

        result.Action.Should().Be(ActionTypes.HideComment);
        result.ShouldHide.Should().BeTrue();
    }

    [Fact]
    public void ComplaintIntent_ActionIsManualReview()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(intent: "complaint"));

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void NegativeSentiment_ActionIsManualReview()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(sentiment: "negative"));

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void PriceInquiry_ActionIsAutoReply()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(intent: "price_inquiry"));

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void PositiveSentiment_ActionIsAutoReply()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(sentiment: "positive"));

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void PositiveIntent_ActionIsAutoReply()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(intent: "positive"));

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void HighConfidenceNeutral_ActionIsAutoReply()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(confidence: 0.7));

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void LowConfidence_ActionIsManualReview()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(MakeEvent(), spamResult, MakeAi(confidence: 0.5));

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void AiUnavailable_LowConfidenceTriggersManualReview()
    {
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var aiResult = new AiAnalysisResult
        {
            Intent = "neutral",
            Sentiment = "neutral",
            Confidence = 0.0
        };

        var result = _sut.Evaluate(MakeEvent(), spamResult, aiResult);

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void Evaluate_SetsIdempotencyKey_FromCommentId()
    {
        var evt = MakeEvent();
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(evt, spamResult, MakeAi());

        result.IdempotencyKey.Should().Be($"fb-comment:{evt.CommentId}");
    }

    [Fact]
    public void Evaluate_CopiesCorrelationIdAndEventId()
    {
        var evt = MakeEvent();
        var spamResult = new SpamDetectorResult { IsSpam = false, SpamType = "none" };
        var result = _sut.Evaluate(evt, spamResult, MakeAi());

        result.CorrelationId.Should().Be(evt.CorrelationId);
        result.EventId.Should().Be(evt.EventId);
    }
}
