using TaxVision.Correspondence.Domain.Inbox;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class EmailThreadTests
{
    [Fact]
    public void NewFromMessage_creates_an_active_thread_with_a_single_message()
    {
        var tenantId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var firstMessageAt = DateTime.UtcNow;

        var result = EmailThread.NewFromMessage(tenantId, customerId, "Subject", "provider-thread-1", firstMessageAt);

        Assert.True(result.IsSuccess);
        var thread = result.Value;
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Equal(1, thread.MessageCount);
        Assert.Equal(firstMessageAt, thread.FirstMessageAtUtc);
        Assert.Equal(firstMessageAt, thread.LastMessageAtUtc);
        Assert.Null(thread.ArchivedAtUtc);
    }

    [Fact]
    public void NewFromMessage_fails_when_customerId_is_empty()
    {
        var result = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.Empty, "Subject", null, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.CustomerIdRequired", result.Error.Code);
    }

    [Fact]
    public void AppendMessage_bumps_message_count_and_last_message_timestamp()
    {
        var thread = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        var secondMessageAt = thread.LastMessageAtUtc.AddMinutes(5);

        var result = thread.AppendMessage(secondMessageAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, thread.MessageCount);
        Assert.Equal(secondMessageAt, thread.LastMessageAtUtc);
    }

    [Fact]
    public void AppendMessage_fails_when_the_thread_is_archived()
    {
        var thread = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;
        thread.Archive();

        var result = thread.AppendMessage(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("EmailThread.Archived", result.Error.Code);
        Assert.Equal(1, thread.MessageCount);
    }

    [Fact]
    public void Archive_sets_status_and_timestamp_and_is_idempotent()
    {
        var thread = EmailThread.NewFromMessage(Guid.NewGuid(), Guid.NewGuid(), "Subject", null, DateTime.UtcNow).Value;

        thread.Archive();
        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        var firstArchivedAt = thread.ArchivedAtUtc;
        Assert.NotNull(firstArchivedAt);

        Thread.Sleep(10);
        thread.Archive();

        Assert.Equal(firstArchivedAt, thread.ArchivedAtUtc);
    }
}
