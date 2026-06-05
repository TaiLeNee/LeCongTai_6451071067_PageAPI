using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using FbApi.CoreService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FbApi.IntegrationTests.Tests.Scenarios;

/// <summary>
/// Integration scenario tests: NormalizedEvent → SpamDetector → RuleEngine → ReplyCommand
/// Tests the full core service pipeline with mocked GeminiService and SpamTrackerService.
/// </summary>
public class CoreServicePipelineTests
{
    private readonly SpamDetectorService _spamDetector;
    private readonly Mock<ISpamTrackerService> _spamTrackerMock;
    private readonly Mock<ILogger<RuleEngineService>> _ruleEngineLoggerMock;
    private readonly RuleEngineService _ruleEngine;

    public CoreServicePipelineTests()
    {
        _spamDetector = new SpamDetectorService();
        _spamTrackerMock = new Mock<ISpamTrackerService>();
        _ruleEngineLoggerMock = new Mock<ILogger<RuleEngineService>>();
        _ruleEngine = new RuleEngineService(_spamTrackerMock.Object, _ruleEngineLoggerMock.Object);
    }

    private static NormalizedEvent MakeEvent(string userId = "user_1", string message = "hello") => new()
    {
        EventId = Guid.NewGuid().ToString(),
        CorrelationId = Guid.NewGuid().ToString(),
        EventType = "comment",
        Source = "facebook",
        PageId = "page_1",
        PostId = "post_1",
        CommentId = "comment_1",
        UserId = userId,
        UserName = "TestUser",
        Message = message
    };

    private ReplyCommand RunPipeline(NormalizedEvent evt, AiAnalysisResult aiResult)
    {
        var spamResult = _spamDetector.Detect(evt);
        if (spamResult.IsSpam)
        {
            _spamTrackerMock.Setup(x => x.GetSpamCount(evt.UserId)).Returns(1);
        }
        _spamTrackerMock.Setup(x => x.IsRepeatedSpammer(evt.UserId)).Returns(false);
        return _ruleEngine.Evaluate(evt, spamResult, aiResult);
    }

    [Fact]
    public void SpamLink_Produces_HideCommentCommand()
    {
        var evt = MakeEvent(message: "check out http://spam.com");
        var aiResult = new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.9 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.HideComment);
        result.ShouldHide.Should().BeTrue();
        result.EventId.Should().Be(evt.EventId);
        result.CorrelationId.Should().Be(evt.CorrelationId);
    }

    [Fact]
    public void ScamSpam_Produces_HideAndReviewCommand()
    {
        var evt = MakeEvent(message: "Get free money now!");
        var aiResult = new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.5 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.HideComment);
        result.ShouldHide.Should().BeTrue();
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void NormalMessage_WithPriceInquiry_ProducesAutoReply()
    {
        var evt = MakeEvent(message: "How much does this cost?");
        var aiResult = new AiAnalysisResult { Intent = "price_inquiry", Sentiment = "neutral", Confidence = 0.9 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void NormalMessage_WithPositiveSentiment_ProducesThankYouReply()
    {
        var evt = MakeEvent(message: "I love this product!");
        var aiResult = new AiAnalysisResult { Intent = "positive", Sentiment = "positive", Confidence = 0.95 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().Contain("Cảm ơn");
    }

    [Fact]
    public void ComplaintMessage_ProducesManualReview()
    {
        var evt = MakeEvent(message: "This is terrible service!");
        var aiResult = new AiAnalysisResult { Intent = "complaint", Sentiment = "negative", Confidence = 0.85 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void NormalMessage_LowConfidence_ProducesManualReview()
    {
        var evt = MakeEvent(message: "Hmm okay");
        var aiResult = new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.3 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.ManualReview);
        result.RequiresManualReview.Should().BeTrue();
    }

    [Fact]
    public void NormalMessage_HighConfidence_ProducesDefaultAutoReply()
    {
        var evt = MakeEvent(message: "Hello there");
        var aiResult = new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.8 };

        var result = RunPipeline(evt, aiResult);

        result.Action.Should().Be(ActionTypes.AutoReply);
        result.ReplyText.Should().NotBeEmpty();
    }

    [Fact]
    public void RepeatedSpammer_ProducesBlacklistCommand()
    {
        var evt = MakeEvent(userId: "spammer_1", message: "Visit http://spam.com");
        _spamTrackerMock.Setup(x => x.IsRepeatedSpammer("spammer_1")).Returns(true);

        var spamResult = _spamDetector.Detect(evt);
        var result = _ruleEngine.Evaluate(evt, spamResult,
            new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.5 });

        result.Action.Should().Be(ActionTypes.BlacklistUser);
        result.InternalBlacklist.Should().BeTrue();
        result.ShouldHide.Should().BeTrue();
    }

    [Fact]
    public void Pipeline_ProducesCorrectIdempotencyKey()
    {
        var evt = MakeEvent(message: "Hello");
        var result = RunPipeline(evt,
            new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.9 });

        result.IdempotencyKey.Should().Be($"fb-comment:{evt.CommentId}");
    }

    [Fact]
    public void Pipeline_CopiesTargetFields()
    {
        var evt = MakeEvent();
        var result = RunPipeline(evt,
            new AiAnalysisResult { Intent = "neutral", Sentiment = "neutral", Confidence = 0.9 });

        result.Target.PageId.Should().Be(evt.PageId);
        result.Target.PostId.Should().Be(evt.PostId);
        result.Target.CommentId.Should().Be(evt.CommentId);
        result.Target.ParentId.Should().Be(evt.ParentId);
    }

    [Fact]
    public void Pipeline_CopiesAiAnalysisFields()
    {
        var evt = MakeEvent();
        var aiResult = new AiAnalysisResult
        {
            Intent = "price_inquiry",
            Sentiment = "positive",
            Confidence = 0.95
        };

        var result = RunPipeline(evt, aiResult);

        result.Intent.Should().Be("price_inquiry");
        result.Sentiment.Should().Be("positive");
        result.Confidence.Should().Be(0.95);
    }
}
