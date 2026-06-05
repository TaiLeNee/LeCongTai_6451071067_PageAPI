using FbApi.Contracts.Models;
using FbApi.WebhookService.Services;
using FluentAssertions;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class NormalizedEventMapperTests
{
    private readonly NormalizedEventMapper _sut = new();

    private static ParsedChange MakeCommentChange() => new()
    {
        PageId = "page_123",
        Field = "feed",
        Timestamp = DateTime.UtcNow,
        PostId = "post_456",
        CommentId = "comment_789",
        ParentId = "parent_012",
        Verb = "add",
        FromId = "user_abc",
        FromName = "John Doe",
        Message = "Great product!",
        Item = "comment",
        CreatedTime = 1700000000,
        RawValueJson = "{\"message\":\"Great product!\"}"
    };

    [Fact]
    public void CommentPayload_MapsToNormalizedEvent_Correctly()
    {
        var change = MakeCommentChange();
        var rawPayload = "{\"object\":\"page\"}";

        var result = _sut.Map(change, rawPayload);

        result.Should().NotBeNull();
        result.PageId.Should().Be("page_123");
        result.PostId.Should().Be("post_456");
        result.CommentId.Should().Be("comment_789");
        result.ParentId.Should().Be("parent_012");
        result.UserId.Should().Be("user_abc");
        result.UserName.Should().Be("John Doe");
        result.Message.Should().Be("Great product!");
        result.EventType.Should().Be("comment");
        result.Source.Should().Be("facebook");
        result.RawPayload.Should().Be(rawPayload);
    }

    [Fact]
    public void RequiredFields_ArePresent()
    {
        var change = MakeCommentChange();
        var result = _sut.Map(change, "{}");

        result.EventId.Should().NotBeNullOrEmpty();
        result.CorrelationId.Should().NotBeNullOrEmpty();
        result.EventType.Should().NotBeNullOrEmpty();
        result.Source.Should().NotBeNullOrEmpty();
        result.PageId.Should().NotBeNullOrEmpty();
        result.CreatedAt.Should().NotBe(default);
    }

    [Fact]
    public void Timestamp_IsISO8601Format()
    {
        var change = MakeCommentChange();
        var result = _sut.Map(change, "{}");

        var isoString = result.CreatedAt.ToString("o");
        isoString.Should().NotBeNullOrEmpty();
        DateTime.TryParse(isoString, out _).Should().BeTrue();
    }

    [Fact]
    public void FeedFieldWithNoCommentId_MapsToPostEventType()
    {
        var change = MakeCommentChange();
        change.CommentId = "";
        change.Field = "feed";
        var result = _sut.Map(change, "{}");

        result.EventType.Should().Be("post");
    }

    [Fact]
    public void FeedFieldWithCommentId_MapsToCommentEventType()
    {
        var change = MakeCommentChange();
        var result = _sut.Map(change, "{}");

        result.EventType.Should().Be("comment");
    }

    [Fact]
    public void MentionField_MapsToMentionEventType()
    {
        var change = MakeCommentChange();
        change.Field = "mention";
        var result = _sut.Map(change, "{}");

        result.EventType.Should().Be("mention");
    }

    [Fact]
    public void MessageField_MapsToMessageEventType()
    {
        var change = MakeCommentChange();
        change.Field = "message";
        var result = _sut.Map(change, "{}");

        result.EventType.Should().Be("message");
    }

    [Fact]
    public void SchemaVersion_IsSet()
    {
        var change = MakeCommentChange();
        var result = _sut.Map(change, "{}");

        result.SchemaVersion.Should().NotBeNullOrEmpty();
    }
}
