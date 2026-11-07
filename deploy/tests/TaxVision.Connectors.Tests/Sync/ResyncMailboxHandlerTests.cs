using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Sync;
using TaxVision.Connectors.Tests.Accounts;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Sync;

public class ResyncMailboxHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime FromUtc = Now.AddDays(-1);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    private static RawMessage Message(string id, string from = "customer@example.com", string subject = "Subject") =>
        new(
            id,
            null,
            $"<{id}@example.com>",
            null,
            [],
            from,
            null,
            ["office@gmail.com"],
            [],
            [],
            subject,
            "Snippet",
            Now,
            [],
            AuthenticationSignals.Unknown
        );

    private static Task<ResyncMailboxCommand?> RunAsync(
        TenantEmailAccount account,
        FakeEmailProviderClient client,
        FakeMessageBus bus,
        string? pageToken = null,
        Guid? tenantId = null,
        TrackingOutboxUnit? unit = null
    )
    {
        var accounts = new FakeTenantEmailAccountRepository();
        accounts.Accounts.Add(account);
        return ResyncMailboxHandler.Handle(
            new ResyncMailboxCommand(tenantId ?? TenantId, account.Id, FromUtc, Now, pageToken, "resync-1"),
            accounts,
            new FakeEmailProviderClientFactory(client),
            unit ?? new TrackingOutboxUnit(new FakeUnitOfWork(), bus),
            NullLogger.Instance,
            CancellationToken.None
        );
    }

    // CON-12: la lista y el contenido vienen del proveedor; la transacción solo cubre lo que se publica.
    [Fact]
    public async Task Resync_CallsTheProviderOutsideTheUnit()
    {
        var account = CreateActiveAccount();
        var bus = new FakeMessageBus();
        var unit = new TrackingOutboxUnit(new FakeUnitOfWork(), bus);
        var openDuringCall = false;
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnListInbox = (_, _, _) =>
            {
                openDuringCall = unit.IsOpen;
                return new InboxMessageIdPage(["m1"], null);
            },
            OnGetMessage = (_, id) => Message(id),
        };

        await RunAsync(account, client, bus, unit: unit);

        Assert.False(openDuringCall);
        Assert.True(unit.Committed);
        Assert.Single(bus.Published.OfType<ConnectorsRawMessageReceivedIntegrationEvent>());
    }

    [Fact]
    public async Task Resync_PublishesReplayEventsAndCascadesNextPage()
    {
        var account = CreateActiveAccount();
        var cursor = ProviderSyncCursor.Create(account.Id, "100", Now).Value;
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnListInbox = (_, _, _) => new InboxMessageIdPage(["m1", "m2"], "p2"),
            // m2 parece un aviso de no entrega: la re-sync no lo reinspecciona.
            OnGetMessage = (_, id) =>
                id == "m2" ? Message(id, "mailer-daemon@example.com", "Undeliverable: hello") : Message(id),
        };
        var bus = new FakeMessageBus();

        var next = await RunAsync(account, client, bus);

        var events = bus.Published.OfType<ConnectorsRawMessageReceivedIntegrationEvent>().ToList();
        Assert.Equal(["m1", "m2"], events.Select(e => e.ProviderMessageId));
        Assert.All(events, e => Assert.True(e.IsReplay));
        Assert.All(events, e => Assert.Equal("resync-1", e.CorrelationId));
        Assert.DoesNotContain(bus.Published, m => m is InspectDeliveryReport);
        Assert.NotNull(next);
        Assert.Equal("p2", next.PageToken);
        Assert.Equal(account.Id, next.AccountId);
        Assert.Equal(FromUtc, next.FromUtc);
        Assert.Equal(Now, next.ToUtc);
        Assert.Equal("100", cursor.CursorValue);
        Assert.Empty(client.ReceivedCursors);
    }

    [Fact]
    public async Task Resync_LastPage_EndsTheChain()
    {
        var account = CreateActiveAccount();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnListInbox = (_, _, token) => new InboxMessageIdPage(["m3"], null),
        };
        var bus = new FakeMessageBus();

        var next = await RunAsync(account, client, bus, pageToken: "p2");

        Assert.Null(next);
        Assert.Equal("p2", client.ListInboxCalls.Single().PageToken);
        Assert.Single(bus.Published.OfType<ConnectorsRawMessageReceivedIntegrationEvent>());
    }

    [Fact]
    public async Task Resync_AccountInError_DoesNothingAndPublishesNoRefreshFailed()
    {
        var account = CreateActiveAccount();
        account.MarkError(AccountErrors.RefreshRejected, Now);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        var bus = new FakeMessageBus();

        var next = await RunAsync(account, client, bus);

        Assert.Null(next);
        Assert.Empty(client.ListInboxCalls);
        Assert.Equal(0, client.GetMessageCalls);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Resync_AccountOfAnotherTenant_DoesNothing()
    {
        var account = CreateActiveAccount();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        var bus = new FakeMessageBus();

        var next = await RunAsync(account, client, bus, tenantId: Guid.NewGuid());

        Assert.Null(next);
        Assert.Empty(client.ListInboxCalls);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Resync_MessageNotFound_IsSkipped()
    {
        var account = CreateActiveAccount();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnListInbox = (_, _, _) => new InboxMessageIdPage(["m1", "gone"], null),
        };
        client.NotFoundOnGetMessageFor.Add("gone");
        var bus = new FakeMessageBus();

        await RunAsync(account, client, bus);

        var evt = Assert.Single(bus.Published.OfType<ConnectorsRawMessageReceivedIntegrationEvent>());
        Assert.Equal("m1", evt.ProviderMessageId);
    }

    [Fact]
    public async Task Resync_TransientFailure_ThrowsSoThePageIsRetried()
    {
        var account = CreateActiveAccount();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnListInbox = (_, _, _) => new InboxMessageIdPage(["m1", "m2"], "p2"),
        };
        client.ThrowOnGetMessageFor.Add("m2");
        var bus = new FakeMessageBus();

        await Assert.ThrowsAsync<EmailProviderException>(() => RunAsync(account, client, bus));

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Resync_RequiresReconnect_StopsTheChainWithoutRetry()
    {
        var account = CreateActiveAccount();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnListInbox = new EmailProviderException("Token revoked.", requiresReconnect: true),
        };
        var bus = new FakeMessageBus();

        var next = await RunAsync(account, client, bus);

        Assert.Null(next);
        Assert.Empty(bus.Published);
    }
}
