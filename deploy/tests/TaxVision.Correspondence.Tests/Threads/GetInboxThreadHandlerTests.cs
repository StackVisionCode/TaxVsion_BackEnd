using TaxVision.Correspondence.Application.Threads;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Tests.Ingest;

namespace TaxVision.Correspondence.Tests.Threads;

// GET /threads/{id}: una fila del Inbox con el mismo gate que el listado.
public sealed class GetInboxThreadHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private static async Task<(
        EmailThread Thread,
        FakeEmailThreadRepository Threads,
        FakeIncomingEmailRepository Emails
    )> SeedAsync()
    {
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(TenantId, customerId, "W-2", null, DateTime.UtcNow).Value;
        var threads = new FakeEmailThreadRepository();
        await threads.AddAsync(thread);
        var emails = new FakeIncomingEmailRepository();
        await emails.AddAsync(
            IncomingEmail
                .Create(
                    TenantId,
                    customerId,
                    thread.Id,
                    Guid.NewGuid(),
                    "gmail",
                    "p-1",
                    EmailAddress.Create("ana@client.com").Value,
                    "Ana",
                    "W-2",
                    "Here it is",
                    DateTime.UtcNow,
                    hasAttachments: false,
                    attachmentCount: 0
                )
                .Value
        );
        return (thread, threads, emails);
    }

    [Fact]
    public async Task A_visible_thread_returns_its_inbox_row()
    {
        var (thread, threads, emails) = await SeedAsync();

        var result = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(TenantId, thread.Id),
            threads,
            emails,
            new TaxVision.Correspondence.Tests.Delivery.FakeDeliveryNoticeRepository(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(thread.Id, result.Value.ThreadId);
        Assert.Equal(1, result.Value.UnreadCount);
        Assert.Equal("ana@client.com", result.Value.LastSenderAddress);
    }

    [Fact]
    public async Task A_thread_of_an_unassigned_customer_or_another_mailbox_is_not_found()
    {
        var (thread, threads, emails) = await SeedAsync();

        var unassigned = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(TenantId, thread.Id, RestrictToUserId: Guid.NewGuid()),
            threads,
            emails,
            new TaxVision.Correspondence.Tests.Delivery.FakeDeliveryNoticeRepository(),
            CancellationToken.None
        );
        threads.HasVisibleMessageResult = false;
        var otherMailbox = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(TenantId, thread.Id, VisibleAccountIds: [Guid.NewGuid()]),
            threads,
            emails,
            new TaxVision.Correspondence.Tests.Delivery.FakeDeliveryNoticeRepository(),
            CancellationToken.None
        );
        var missing = await GetInboxThreadHandler.Handle(
            new GetInboxThreadQuery(TenantId, Guid.NewGuid()),
            threads,
            emails,
            new TaxVision.Correspondence.Tests.Delivery.FakeDeliveryNoticeRepository(),
            CancellationToken.None
        );

        Assert.All(
            new[] { unassigned, otherMailbox, missing },
            r => Assert.Equal("EmailThread.NotFound", r.Error.Code)
        );
    }
}
