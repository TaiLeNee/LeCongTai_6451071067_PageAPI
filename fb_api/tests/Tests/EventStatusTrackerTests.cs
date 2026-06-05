using FbApi.CoreService.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace FbApi.IntegrationTests.Tests;

public class EventStatusTrackerTests
{
    private readonly Mock<ILogger<EventStatusTracker>> _loggerMock;
    private readonly EventStatusTracker _sut;

    public EventStatusTrackerTests()
    {
        _loggerMock = new Mock<ILogger<EventStatusTracker>>();
        _sut = new EventStatusTracker(_loggerMock.Object);
    }

    [Fact]
    public void SetReceived_GetStatus_ReturnsReceived()
    {
        _sut.SetReceived("evt_001");

        var status = _sut.GetStatus("evt_001");

        status.Should().NotBeNull();
        status!.Status.Should().Be("received");
        status.EventId.Should().Be("evt_001");
    }

    [Fact]
    public void StatusTransition_ReceivedToProcessing()
    {
        _sut.SetReceived("evt_002");
        _sut.SetProcessing("evt_002");

        var status = _sut.GetStatus("evt_002");

        status.Should().NotBeNull();
        status!.Status.Should().Be("processing");
    }

    [Fact]
    public void StatusTransition_ProcessingToCommandPublished()
    {
        _sut.SetReceived("evt_003");
        _sut.SetProcessing("evt_003");
        _sut.SetCommandPublished("evt_003");

        var status = _sut.GetStatus("evt_003");

        status.Should().NotBeNull();
        status!.Status.Should().Be("command_published");
    }

    [Fact]
    public void StatusTransition_CommandPublishedToFailed()
    {
        _sut.SetReceived("evt_004");
        _sut.SetProcessing("evt_004");
        _sut.SetCommandPublished("evt_004");
        _sut.SetFailed("evt_004", "Facebook API timeout");

        var status = _sut.GetStatus("evt_004");

        status.Should().NotBeNull();
        status!.Status.Should().Be("failed");
        status.Error.Should().Be("Facebook API timeout");
    }

    [Fact]
    public void FullTransition_ReceivedProcessingCommandPublishedFailed()
    {
        const string eventId = "evt_full";

        _sut.SetReceived(eventId);
        _sut.GetStatus(eventId)!.Status.Should().Be("received");

        _sut.SetProcessing(eventId);
        _sut.GetStatus(eventId)!.Status.Should().Be("processing");

        _sut.SetCommandPublished(eventId);
        _sut.GetStatus(eventId)!.Status.Should().Be("command_published");

        _sut.SetFailed(eventId, "API error");
        _sut.GetStatus(eventId)!.Status.Should().Be("failed");
        _sut.GetStatus(eventId)!.Error.Should().Be("API error");
    }

    [Fact]
    public void UnknownEventId_ReturnsNull()
    {
        var status = _sut.GetStatus("nonexistent_event");

        status.Should().BeNull();
    }

    [Fact]
    public void SetFailed_WithoutPriorStatus_CreatesNewEntry()
    {
        _sut.SetFailed("evt_new", "Unexpected error");

        var status = _sut.GetStatus("evt_new");

        status.Should().NotBeNull();
        status!.Status.Should().Be("failed");
        status.Error.Should().Be("Unexpected error");
    }

    [Fact]
    public void MultipleEvents_TrackedIndependently()
    {
        _sut.SetReceived("evt_a");
        _sut.SetProcessing("evt_b");
        _sut.SetCommandPublished("evt_c");

        _sut.GetStatus("evt_a")!.Status.Should().Be("received");
        _sut.GetStatus("evt_b")!.Status.Should().Be("processing");
        _sut.GetStatus("evt_c")!.Status.Should().Be("command_published");
    }

    [Fact]
    public void UpdatedAt_ChangesOnStatusUpdate()
    {
        _sut.SetReceived("evt_time");
        var firstTime = _sut.GetStatus("evt_time")!.UpdatedAt;

        // Small delay to ensure time difference
        Thread.Sleep(10);
        _sut.SetProcessing("evt_time");
        var secondTime = _sut.GetStatus("evt_time")!.UpdatedAt;

        secondTime.Should().BeOnOrAfter(firstTime);
    }
}
