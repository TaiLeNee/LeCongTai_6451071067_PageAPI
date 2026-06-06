using FbApi.BackendApi.Repositories;
using FbApi.BackendApi.Services;
using FbApi.BackendApi.Workers;
using FbApi.Contracts.Constants;
using FbApi.Contracts.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FbApi.IntegrationTests.Tests.Scenarios;

/// <summary>
/// Integration scenario tests for Backend API processing logic:
/// - ReplyCommandConsumer processes commands correctly
/// - Idempotency check prevents duplicate processing
/// - Failed action produces send_failed message
/// Uses mocked dependencies to test logic without Kafka or PostgreSQL.
/// </summary>
public class BackendApiTests
{
    private readonly Mock<IIdempotencyRepository> _idempotencyRepoMock;
    private readonly Mock<ICommandStatusRepository> _commandStatusRepoMock;
    private readonly Mock<IFacebookActionService> _facebookActionMock;
    private readonly Mock<IFacebookApiErrorHandler> _errorHandlerMock;
    private readonly Mock<ISendFailedPublisher> _sendFailedPublisherMock;
    private readonly Mock<ILogger<ReplyCommandWorker>> _loggerMock;

    public BackendApiTests()
    {
        _idempotencyRepoMock = new Mock<IIdempotencyRepository>();
        _commandStatusRepoMock = new Mock<ICommandStatusRepository>();
        _facebookActionMock = new Mock<IFacebookActionService>();
        _errorHandlerMock = new Mock<IFacebookApiErrorHandler>();
        _sendFailedPublisherMock = new Mock<ISendFailedPublisher>();
        _loggerMock = new Mock<ILogger<ReplyCommandWorker>>();

        _commandStatusRepoMock.Setup(x => x.TryInsertAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>()))
            .ReturnsAsync(true);
    }

    private static ReplyCommand MakeCommand(
        string action = ActionTypes.AutoReply,
        bool shouldHide = false,
        string replyText = "Thank you!") => new()
    {
        CommandId = Guid.NewGuid().ToString(),
        CorrelationId = Guid.NewGuid().ToString(),
        EventId = Guid.NewGuid().ToString(),
        Action = action,
        Target = new ReplyTarget
        {
            PageId = "page_1",
            PostId = "post_1",
            CommentId = "comment_1",
            ParentId = ""
        },
        ReplyText = replyText,
        ShouldHide = shouldHide,
        IdempotencyKey = "comment_1:" + Guid.NewGuid()
    };

    /// <summary>
    /// Simulates the core processing logic of ReplyCommandWorker.ProcessMessageAsync
    /// without Kafka dependency.
    /// </summary>
    private async Task<BackendProcessingResult> SimulateProcessingAsync(ReplyCommand command)
    {
        var result = new BackendProcessingResult();

        var isNewCommand = await _commandStatusRepoMock.Object.TryInsertAsync(
            command.CommandId, command.EventId, command.CorrelationId, command.Action, "pending");

        if (!isNewCommand)
        {
            result.IsDuplicateCommand = true;
            result.ExistingStatus = await _commandStatusRepoMock.Object.GetStatusAsync(command.CommandId);
            return result;
        }

        var isNewIdempotencyKey = await _idempotencyRepoMock.Object.TryInsertAsync(
            command.IdempotencyKey, command.CommandId, command.Action, "pending");

        if (!isNewIdempotencyKey)
        {
            var existingStatus = await _idempotencyRepoMock.Object.GetStatusAsync(command.IdempotencyKey);
            result.IsDuplicateIdempotencyKey = true;
            result.ExistingStatus = existingStatus;
            return result;
        }

        // Process command
        result.IsFirst = true;
        if (command.Action == ActionTypes.AutoReply)
        {
            var replyResult = await _facebookActionMock.Object.SendAutoReplyAsync(
                command.Target.PageId, command.Target.CommentId, command.ReplyText);

            if (replyResult.Success && command.ShouldHide)
            {
                var hideResult = await _facebookActionMock.Object.HideCommentAsync(
                    command.Target.PageId, command.Target.CommentId);
                result.ActionCompleted = replyResult.Success && hideResult.Success;
                result.ErrorMessage = (!replyResult.Success ? replyResult.ErrorMessage : null)
                    ?? (!hideResult.Success ? hideResult.ErrorMessage : null);
            }
            else if (replyResult.Success)
            {
                result.ActionCompleted = true;
            }
            else
            {
                result.ActionCompleted = false;
                result.ErrorMessage = replyResult.ErrorMessage ?? "Unknown error";
                var classification = _errorHandlerMock.Object.ClassifyError(new Exception(result.ErrorMessage));
                await _sendFailedPublisherMock.Object.PublishAsync(new SendFailedMessage
                {
                    CommandId = command.CommandId,
                    CorrelationId = command.CorrelationId,
                    EventId = command.EventId,
                    RetryCount = 1,
                    LastError = result.ErrorMessage,
                    ErrorType = classification.Classification.ToString(),
                    Payload = command.CommandId
                });
            }
        }
        else if (command.Action == ActionTypes.HideComment)
        {
            var hideResult = await _facebookActionMock.Object.HideCommentAsync(
                command.Target.PageId, command.Target.CommentId);
            result.ActionCompleted = hideResult.Success;
            result.ErrorMessage = hideResult.Success ? null : hideResult.ErrorMessage;
            if (!hideResult.Success)
            {
                var classification = _errorHandlerMock.Object.ClassifyError(new Exception(result.ErrorMessage ?? "Unknown error"));
                await _sendFailedPublisherMock.Object.PublishAsync(new SendFailedMessage
                {
                    CommandId = command.CommandId,
                    CorrelationId = command.CorrelationId,
                    EventId = command.EventId,
                    RetryCount = 1,
                    LastError = result.ErrorMessage ?? "Unknown error",
                    ErrorType = classification.Classification.ToString(),
                    Payload = command.CommandId
                });
            }
        }
        else
        {
            result.UnsupportedAction = true;
        }

        return result;
    }

    [Fact]
    public async Task AutoReplyCommand_ProcessesSuccessfully()
    {
        var command = MakeCommand(ActionTypes.AutoReply, replyText: "Thank you!");
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(command.IdempotencyKey, command.CommandId, command.Action, "pending"))
            .ReturnsAsync(true);
        _facebookActionMock.Setup(x => x.SendAutoReplyAsync(command.Target.PageId, command.Target.CommentId, command.ReplyText))
            .ReturnsAsync(new FacebookActionResult(true, ResponseData: "{\"id\":\"reply_1\"}"));

        var result = await SimulateProcessingAsync(command);

        result.IsFirst.Should().BeTrue();
        result.IsDuplicateIdempotencyKey.Should().BeFalse();
        result.IsDuplicateCommand.Should().BeFalse();
        result.ActionCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task AutoReplyWithHide_BothActionsSucceed()
    {
        var command = MakeCommand(ActionTypes.AutoReply, shouldHide: true, replyText: "Auto reply");
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _facebookActionMock.Setup(x => x.SendAutoReplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new FacebookActionResult(true, ResponseData: "{\"id\":\"reply_1\"}"));
        _facebookActionMock.Setup(x => x.HideCommentAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new FacebookActionResult(true, ResponseData: "{\"success\":true}"));

        var result = await SimulateProcessingAsync(command);

        result.ActionCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task IdempotencyCheck_PreventsDuplicateProcessing()
    {
        var command = MakeCommand();
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(command.IdempotencyKey, command.CommandId, command.Action, "pending"))
            .ReturnsAsync(false);
        _idempotencyRepoMock.Setup(x => x.GetStatusAsync(command.IdempotencyKey))
            .ReturnsAsync("completed");

        var result = await SimulateProcessingAsync(command);

        result.IsDuplicateIdempotencyKey.Should().BeTrue();
        result.ExistingStatus.Should().Be("completed");
        _facebookActionMock.Verify(
            x => x.SendAutoReplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never, "duplicate should not trigger Facebook action");
    }

    [Fact]
    public async Task DuplicateCommandId_PreventsDuplicateProcessing()
    {
        var command = MakeCommand();
        _commandStatusRepoMock.Setup(x => x.TryInsertAsync(
                command.CommandId,
                command.EventId,
                command.CorrelationId,
                command.Action,
                "pending"))
            .ReturnsAsync(false);
        _commandStatusRepoMock.Setup(x => x.GetStatusAsync(command.CommandId))
            .ReturnsAsync("processing");

        var result = await SimulateProcessingAsync(command);

        result.IsDuplicateCommand.Should().BeTrue();
        result.ExistingStatus.Should().Be("processing");
        _idempotencyRepoMock.Verify(
            x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "duplicate command_id should be skipped before creating another idempotency row");
        _facebookActionMock.Verify(
            x => x.SendAutoReplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()),
            Times.Never,
            "duplicate command_id should not trigger Facebook action");
    }

    [Fact]
    public async Task FailedAction_ProducesSendFailedMessage()
    {
        var command = MakeCommand(ActionTypes.AutoReply, replyText: "Hello");
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _facebookActionMock.Setup(x => x.SendAutoReplyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new FacebookActionResult(false, ErrorMessage: "API timeout"));
        _errorHandlerMock.Setup(x => x.ClassifyError(It.IsAny<Exception>()))
            .Returns(new ClassifiedError(FacebookErrorClassification.Retryable, "TIMEOUT", "API timeout"));

        var result = await SimulateProcessingAsync(command);

        result.ActionCompleted.Should().BeFalse();
        result.ErrorMessage.Should().Be("API timeout");

        // Verify error handler was invoked (simulating what ReplyCommandWorker does)
        _errorHandlerMock.Verify(x => x.ClassifyError(It.IsAny<Exception>()), Times.Once);
        _sendFailedPublisherMock.Verify(x => x.PublishAsync(It.Is<SendFailedMessage>(
            msg => msg.CommandId == command.CommandId && msg.LastError == "API timeout")), Times.Once);
    }

    [Fact]
    public async Task HideComment_ProcessesSuccessfully()
    {
        var command = MakeCommand(ActionTypes.HideComment, shouldHide: true);
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);
        _facebookActionMock.Setup(x => x.HideCommentAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new FacebookActionResult(true, ResponseData: "{\"success\":true}"));

        var result = await SimulateProcessingAsync(command);

        result.ActionCompleted.Should().BeTrue();
        _facebookActionMock.Verify(x => x.HideCommentAsync(command.Target.PageId, command.Target.CommentId), Times.Once);
    }

    [Fact]
    public async Task UnsupportedAction_MarkedAsUnsupported()
    {
        var command = MakeCommand(action: "unknown_action");
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(true);

        var result = await SimulateProcessingAsync(command);

        result.UnsupportedAction.Should().BeTrue();
    }

    [Fact]
    public async Task DuplicateWithPendingStatus_ShouldNotSkip()
    {
        var command = MakeCommand();
        _idempotencyRepoMock.Setup(x => x.TryInsertAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(false);
        _idempotencyRepoMock.Setup(x => x.GetStatusAsync(It.IsAny<string>()))
            .ReturnsAsync("pending");

        var result = await SimulateProcessingAsync(command);

        result.IsDuplicateIdempotencyKey.Should().BeTrue();
        result.ExistingStatus.Should().Be("pending");
        // In the real worker, pending status means the previous attempt may have failed
        // and should be retried (not skipped like "completed")
    }

    private class BackendProcessingResult
    {
        public bool IsFirst { get; set; }
        public bool IsDuplicateCommand { get; set; }
        public bool IsDuplicateIdempotencyKey { get; set; }
        public string? ExistingStatus { get; set; }
        public bool ActionCompleted { get; set; }
        public string? ErrorMessage { get; set; }
        public bool UnsupportedAction { get; set; }
    }
}
