using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using Microsoft.Extensions.Logging;

namespace FbApi.CoreService.Services;

public interface IRuleEngineService
{
    ReplyCommand Evaluate(NormalizedEvent evt, SpamDetectorResult spamResult, AiAnalysisResult aiResult);
}

public class RuleEngineService : IRuleEngineService
{
    private readonly ISpamTrackerService _spamTracker;
    private readonly ILogger<RuleEngineService> _logger;

    public RuleEngineService(ISpamTrackerService spamTracker, ILogger<RuleEngineService> logger)
    {
        _spamTracker = spamTracker;
        _logger = logger;
    }

    public ReplyCommand Evaluate(NormalizedEvent evt, SpamDetectorResult spamResult, AiAnalysisResult aiResult)
    {
        var cmd = new ReplyCommand
        {
            CorrelationId = evt.CorrelationId,
            EventId = evt.EventId,
            Target = new ReplyTarget
            {
                PageId = evt.PageId,
                PostId = evt.PostId,
                CommentId = evt.CommentId,
                ParentId = evt.ParentId
            },
            Intent = aiResult.Intent,
            Sentiment = aiResult.Sentiment,
            Confidence = aiResult.Confidence,
            Reason = aiResult.Intent,
            IdempotencyKey = $"fb-comment:{evt.CommentId}"
        };

        // Rule 1: Blacklisted user → no reply, internal action
        if (_spamTracker.IsBlacklisted(evt.UserId))
        {
            _logger.LogInformation("User {UserId} is blacklisted - no reply", evt.UserId);
            cmd.Action = ActionTypes.BlacklistUser;
            cmd.InternalBlacklist = true;
            cmd.RequiresManualReview = true;
            cmd.Reason = "User is blacklisted";
            return cmd;
        }

        // Rule 2: Malicious spam → hide + manual review
        if (spamResult.IsSpam && spamResult.SpamType == "scam")
        {
            _logger.LogInformation("Malicious spam detected from {UserId} - hiding + review", evt.UserId);
            cmd.Action = ActionTypes.HideComment;
            cmd.ShouldHide = true;
            cmd.RequiresManualReview = true;
            cmd.Reason = $"Malicious spam detected: {spamResult.SpamType}";
            return cmd;
        }

        // Rule 3: Repeated spam >= 3x/24h → blacklist
        if (spamResult.IsSpam && _spamTracker.IsRepeatedSpammer(evt.UserId))
        {
            _logger.LogWarning("User {UserId} is a repeated spammer - blacklisting", evt.UserId);
            cmd.Action = ActionTypes.BlacklistUser;
            cmd.ShouldHide = true;
            cmd.InternalBlacklist = true;
            cmd.Reason = "Repeated spammer (>=3 in 24h)";
            _spamTracker.BlacklistUser(evt.UserId);
            return cmd;
        }

        // Rule 4: Normal spam → hide
        if (spamResult.IsSpam)
        {
            _logger.LogInformation("Spam detected from {UserId} - hiding", evt.UserId);
            cmd.Action = ActionTypes.HideComment;
            cmd.ShouldHide = true;
            cmd.Reason = $"Spam detected: {spamResult.SpamType}";
            return cmd;
        }

        // Rule 5: Complaint/negative → manual review
        if (aiResult.Intent == "complaint" || aiResult.Sentiment == "negative")
        {
            _logger.LogInformation("Complaint/negative from {UserId} - manual review", evt.UserId);
            cmd.Action = ActionTypes.ManualReview;
            cmd.RequiresManualReview = true;
            cmd.Reason = $"Complaint/negative sentiment: {aiResult.Intent}/{aiResult.Sentiment}";
            return cmd;
        }

        // Rule 6: Price inquiry → auto reply
        if (aiResult.Intent == "price_inquiry")
        {
            _logger.LogInformation("Price inquiry from {UserId} - auto reply", evt.UserId);
            cmd.Action = ActionTypes.AutoReply;
            cmd.ReplyText = "Cảm ơn bạn đã quan tâm! Vui lòng inbox để nhận báo giá chi tiết.";
            cmd.Reason = "Price inquiry - auto reply";
            return cmd;
        }

        // Rule 7: Positive → thank you
        if (aiResult.Sentiment == "positive" || aiResult.Intent == "positive")
        {
            _logger.LogInformation("Positive comment from {UserId} - thank you", evt.UserId);
            cmd.Action = ActionTypes.AutoReply;
            cmd.ReplyText = "Cảm ơn bạn! Rất vui vì bạn quan tâm ❤️";
            cmd.Reason = "Positive - thank you";
            return cmd;
        }

        // Rule 8: Default → based on confidence
        if (aiResult.Confidence >= 0.7)
        {
            _logger.LogInformation("High confidence default from {UserId} - auto reply", evt.UserId);
            cmd.Action = ActionTypes.AutoReply;
            cmd.ReplyText = "Cảm ơn bạn đã liên hệ! Chúng tôi sẽ phản hồi sớm nhất.";
            cmd.Reason = "Default high-confidence response";
        }
        else
        {
            _logger.LogInformation("Low confidence default from {UserId} - manual review", evt.UserId);
            cmd.Action = ActionTypes.ManualReview;
            cmd.RequiresManualReview = true;
            cmd.Reason = "Low confidence - manual review";
        }

        return cmd;
    }
}
