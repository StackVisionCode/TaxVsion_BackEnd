using TaxVision.Correspondence.Domain.Compose;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Tests.Compose;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Domain;

public sealed class EmailThreadIngestionTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();
    private static readonly DateTime ReceivedAt = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private static InboundThreadMessage Message(string? providerThreadId = "G1", Guid? tenantId = null) =>
        new(tenantId ?? TenantId, CustomerId, "Hello", providerThreadId, ReceivedAt);

    private static EmailThread Thread(string? providerThreadId = "G1") =>
        EmailThread.NewFromMessage(TenantId, CustomerId, "Hello", providerThreadId, ReceivedAt.AddHours(-1)).Value;

    [Fact]
    public void Attach_ArchivedMatch_ReopensAndAppends()
    {
        var thread = Thread();
        thread.Archive();

        var attachment = EmailThreadIngestion.Attach(thread, null, Message()).Value;
        var reopened = attachment.Commit();

        Assert.Same(thread, attachment.Thread);
        Assert.False(attachment.IsNewThread);
        Assert.True(reopened.Value);
        Assert.Equal(EmailThreadStatus.Active, thread.Status);
        Assert.Equal(2, thread.MessageCount);
    }

    [Fact]
    public void Attach_ActiveMatch_AppendsWithoutReopening()
    {
        var thread = Thread();

        var reopened = EmailThreadIngestion.Attach(thread, null, Message()).Value.Commit();

        Assert.False(reopened.Value);
        Assert.Equal(2, thread.MessageCount);
    }

    [Fact]
    public void Attach_NoMatch_CreatesThread()
    {
        var attachment = EmailThreadIngestion.Attach(null, null, Message()).Value;
        var reopened = attachment.Commit();

        Assert.True(attachment.IsNewThread);
        Assert.False(reopened.Value);
        Assert.Equal("G1", attachment.Thread.ProviderThreadId);
        Assert.Equal(CustomerId, attachment.Thread.CustomerId);
        Assert.Equal(1, attachment.Thread.MessageCount);
    }

    [Fact]
    public void Attach_InvalidMessage_Fails()
    {
        var result = EmailThreadIngestion.Attach(null, null, Message(tenantId: Guid.Empty));

        Assert.True(result.IsFailure);
    }

    [Fact]
    public void A_matched_thread_is_untouched_until_commit()
    {
        var thread = Thread();
        thread.Archive();

        EmailThreadIngestion.Attach(thread, null, Message());

        Assert.Equal(EmailThreadStatus.Archived, thread.Status);
        Assert.Equal(1, thread.MessageCount);
    }

    [Fact]
    public async Task Attach_UnthreadedSentDraft_CreatesItsThreadAndAdoptsTheProviderId()
    {
        var draft = await SentDraftAsync();

        var attachment = EmailThreadIngestion.Attach(null, draft, Message()).Value;
        attachment.Commit();

        Assert.True(attachment.IsNewThread);
        Assert.Equal(draft.Id, attachment.Thread.Id);
        Assert.Equal(draft.Id, draft.EmailThreadId);
        Assert.Equal("G1", attachment.Thread.ProviderThreadId);
        Assert.Equal(1, attachment.Thread.MessageCount);
    }

    [Fact]
    public async Task A_sent_draft_is_not_linked_until_commit()
    {
        var draft = await SentDraftAsync();

        EmailThreadIngestion.Attach(null, draft, Message());

        Assert.Null(draft.EmailThreadId);
    }

    private static Task<Draft> SentDraftAsync() =>
        ThreadResolverTests.SeedSentDraftAsync(new FakeDraftRepository(), TenantId, CustomerId, "pm.dom@tenant.test");
}
