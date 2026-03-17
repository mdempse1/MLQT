using MLQT.Services;
using ModelicaParser.DataTypes;

namespace MLQT.Services.Tests;

/// <summary>
/// Unit tests for the CodeReviewService class.
/// </summary>
public class CodeReviewServiceTests
{
    [Fact]
    public void LogMessages_InitiallyEmpty()
    {
        var service = new CodeReviewService();

        Assert.Empty(service.LogMessages);
    }

    [Fact]
    public void AddLogMessage_AddsMessageToCollection()
    {
        var service = new CodeReviewService();
        var message = new LogMessage("TestModel", "Info", 1, "Test summary");

        service.AddLogMessage(message);

        Assert.Single(service.LogMessages);
        Assert.Equal("TestModel", service.LogMessages[0].ModelName);
        Assert.Equal("Test summary", service.LogMessages[0].Summary);
    }

    [Fact]
    public void AddLogMessage_FiresOnLogMessagesChangedEvent()
    {
        var service = new CodeReviewService();
        var eventFired = false;
        service.OnLogMessagesChanged += () => eventFired = true;

        service.AddLogMessage(new LogMessage("Test", "Info", 1, "Test"));

        Assert.True(eventFired);
    }

    [Fact]
    public void AddLogMessages_AddsMultipleMessages()
    {
        var service = new CodeReviewService();
        var messages = new List<LogMessage>
        {
            new LogMessage("Model1", "Info", 1, "Message1"),
            new LogMessage("Model2", "Warning", 2, "Message2"),
            new LogMessage("Model3", "Error", 3, "Message3")
        };

        service.AddLogMessages(messages);

        Assert.Equal(3, service.LogMessages.Count);
        Assert.Equal("Model1", service.LogMessages[0].ModelName);
        Assert.Equal("Model2", service.LogMessages[1].ModelName);
        Assert.Equal("Model3", service.LogMessages[2].ModelName);
    }

    [Fact]
    public void AddLogMessages_FiresOnLogMessagesChangedEventOnce()
    {
        var service = new CodeReviewService();
        var eventCount = 0;
        service.OnLogMessagesChanged += () => eventCount++;
        var messages = new List<LogMessage>
        {
            new LogMessage("Model1", "Info", 1, "Message1"),
            new LogMessage("Model2", "Info", 2, "Message2")
        };

        service.AddLogMessages(messages);

        Assert.Equal(1, eventCount);
    }

    [Fact]
    public void ClearLogMessages_RemovesAllMessages()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Info", 1, "Message1"));
        service.AddLogMessage(new LogMessage("Model2", "Info", 2, "Message2"));

        service.ClearLogMessages();

        Assert.Empty(service.LogMessages);
    }

    [Fact]
    public void ClearLogMessages_FiresOnLogMessagesChangedEvent()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Test", "Info", 1, "Test"));
        var eventFired = false;
        service.OnLogMessagesChanged += () => eventFired = true;

        service.ClearLogMessages();

        Assert.True(eventFired);
    }

    [Fact]
    public void RemoveLogMessage_RemovesSpecificMessage()
    {
        var service = new CodeReviewService();
        var message1 = new LogMessage("Model1", "Info", 1, "Message1");
        var message2 = new LogMessage("Model2", "Info", 2, "Message2");
        service.AddLogMessage(message1);
        service.AddLogMessage(message2);

        service.RemoveLogMessage(message1);

        Assert.Single(service.LogMessages);
        Assert.Equal("Model2", service.LogMessages[0].ModelName);
    }

    [Fact]
    public void RemoveLogMessage_DoesNothingForNullMessage()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Test", "Info", 1, "Test"));

        service.RemoveLogMessage(null!);

        Assert.Single(service.LogMessages);
    }

    [Fact]
    public void RemoveLogMessage_DoesNothingForNonExistentMessage()
    {
        var service = new CodeReviewService();
        var message1 = new LogMessage("Model1", "Info", 1, "Message1");
        var message2 = new LogMessage("Model2", "Info", 2, "Message2");
        service.AddLogMessage(message1);

        service.RemoveLogMessage(message2);

        Assert.Single(service.LogMessages);
    }

    [Fact]
    public void LogMessages_ReturnsNewListEachTime()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Test", "Info", 1, "Test"));

        var list1 = service.LogMessages;
        var list2 = service.LogMessages;

        Assert.NotSame(list1, list2);
    }

    [Fact]
    public void LogMessages_ModifyingReturnedListDoesNotAffectService()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Test", "Info", 1, "Test"));

        var list = service.LogMessages;
        list.Clear();

        Assert.Single(service.LogMessages);
    }

    [Fact]
    public void RemoveLogMessagesForModels_RemovesMessagesForSpecifiedModels()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Warning", 1, "Issue in Model1"));
        service.AddLogMessage(new LogMessage("Model2", "Warning", 2, "Issue in Model2"));
        service.AddLogMessage(new LogMessage("Model3", "Warning", 3, "Issue in Model3"));

        service.RemoveLogMessagesForModels(new[] { "Model1", "Model2" });

        Assert.Single(service.LogMessages);
        Assert.Equal("Model3", service.LogMessages[0].ModelName);
    }

    [Fact]
    public void RemoveLogMessagesForModels_FiresEventWhenMessagesRemoved()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Warning", 1, "Issue"));
        var eventFired = false;
        service.OnLogMessagesChanged += () => eventFired = true;

        service.RemoveLogMessagesForModels(new[] { "Model1" });

        Assert.True(eventFired);
    }

    [Fact]
    public void RemoveLogMessagesForModels_DoesNotFireEventWhenNoMessagesRemoved()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Warning", 1, "Issue"));
        var eventFired = false;
        service.OnLogMessagesChanged += () => eventFired = true;

        service.RemoveLogMessagesForModels(new[] { "NonExistentModel" });

        Assert.False(eventFired);
    }

    [Fact]
    public void RemoveLogMessagesForModels_WithEmptyList_DoesNothing()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Warning", 1, "Issue"));
        var eventFired = false;
        service.OnLogMessagesChanged += () => eventFired = true;

        service.RemoveLogMessagesForModels(new string[0]);

        Assert.Single(service.LogMessages);
        Assert.False(eventFired);
    }

    [Fact]
    public void RemoveLogMessagesForModels_RemovesAllMessagesForModel()
    {
        var service = new CodeReviewService();
        service.AddLogMessage(new LogMessage("Model1", "Warning", 1, "Issue 1"));
        service.AddLogMessage(new LogMessage("Model1", "Error", 2, "Issue 2"));
        service.AddLogMessage(new LogMessage("Model2", "Warning", 3, "Other issue"));

        service.RemoveLogMessagesForModels(new[] { "Model1" });

        Assert.Single(service.LogMessages);
        Assert.Equal("Model2", service.LogMessages[0].ModelName);
    }
}
