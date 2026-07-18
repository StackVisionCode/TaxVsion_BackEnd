using BuildingBlocks.Messaging.EmailIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Tests.OAuth;
using TaxVision.Connectors.Tests.Watch;

namespace TaxVision.Connectors.Tests.Sync;

public class ReconcileAccountHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 18, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount(string email, ProviderCode providerCode)
    {
        var account = TenantEmailAccount.Create(TenantId, email, providerCode, Guid.NewGuid(), Now).Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    [Fact]
    public async Task Handle_WithUnknownAccountId_ReturnsFailure()
    {
        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(Guid.NewGuid()),
            new FakeTenantEmailAccountRepository(),
            new FakeProviderWatchSubscriptionRepository(),
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Gmail)),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenHistoryFetchFails_ReturnsFailure()
    {
        var account = CreateActiveAccount("office@gmail.com", ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnGetHistory = new EmailProviderException("Gmail API returned HTTP 500."),
        };

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        subscriptionRepository.Subscriptions.Add(
            ProviderWatchSubscription
                .Create(account.Id, ProviderCode.Gmail, "watch-history-1", "topic", Now.AddDays(7), Now)
                .Value
        );

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            subscriptionRepository,
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("RawMessageSync.HistoryFetchFailed", result.Error.Code);
    }

    /// <summary>
    /// Gmail/Graph, primer pase (sin cursor previo) — mismo escenario que un push handler
    /// "primer push tras conectar", solo que disparado por reconciliación. CursorWasSeeded debe
    /// quedar true: ReconciliationJob usa esto para NO tratar los mensajes encontrados como
    /// "recuperados de un push perdido" (es simplemente el catch-up inicial esperado).
    /// </summary>
    [Fact]
    public async Task Handle_Gmail_WithNoExistingCursor_SeedsFromWatchSubscriptionAndMarksCursorWasSeeded()
    {
        var account = CreateActiveAccount("office@gmail.com", ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        subscriptionRepository.Subscriptions.Add(
            ProviderWatchSubscription
                .Create(account.Id, ProviderCode.Gmail, "watch-history-1", "topic", Now.AddDays(7), Now)
                .Value
        );

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg1", "msg2"], "history-2", false),
        };
        var bus = new FakeMessageBus();

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            subscriptionRepository,
            cursorRepository,
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("watch-history-1", client.ReceivedCursors[0]);
        Assert.True(result.Value.CursorWasSeeded);
        Assert.False(result.Value.Skipped);
        Assert.Equal(2, result.Value.MessagesFound);
        Assert.Equal(2, bus.Published.Count);
        Assert.All(bus.Published, e => Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(e));
    }

    /// <summary>
    /// Gmail, cursor YA existía (no es el primer sync) y el pase encuentra mensajes nuevos — este es
    /// exactamente el escenario que representa un push perdido: CursorWasSeeded debe quedar false
    /// para que ReconciliationJob lo cuente como "recuperado".
    /// </summary>
    [Fact]
    public async Task Handle_Gmail_WithExistingCursorAndNewMessages_DoesNotMarkCursorWasSeeded()
    {
        var account = CreateActiveAccount("office@gmail.com", ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        cursorRepository.Cursors.Add(
            TaxVision.Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "history-5", Now).Value
        );

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg-missed"], "history-6", false),
        };
        var bus = new FakeMessageBus();

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
            cursorRepository,
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("history-5", client.ReceivedCursors[0]);
        Assert.False(result.Value.CursorWasSeeded);
        Assert.Equal(1, result.Value.MessagesFound);
        Assert.Single(bus.Published);
    }

    /// <summary>IMAP nunca tiene push — el primer pase de reconciliación ES su primer sync: cursor null-seeded, fetch completo del inbox vía SearchQuery.All (ImapClient), sin tocar ProviderWatchSubscription (IMAP no crea una — SetupWatchHandler).</summary>
    [Fact]
    public async Task Handle_Imap_WithNoExistingCursor_SeedsNullCursorWithoutTouchingWatchSubscriptions()
    {
        var account = CreateActiveAccount("office@ownserver.com", ProviderCode.Imap);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var cursorRepository = new FakeProviderSyncCursorRepository();
        var client = new FakeEmailProviderClient(ProviderCode.Imap)
        {
            OnGetHistory = (_, _) => new HistoryPage(["1", "2", "3"], "1:3", false),
        };
        var bus = new FakeMessageBus();

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            subscriptionRepository,
            cursorRepository,
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(client.ReceivedCursors[0]);
        Assert.True(result.Value.CursorWasSeeded);
        Assert.Equal(3, result.Value.MessagesFound);
        Assert.Empty(subscriptionRepository.Subscriptions); // Never queried/created for IMAP.
        Assert.Equal(3, bus.Published.Count);
    }

    /// <summary>Serializa contra un sync ya en vuelo — mismo lock namespace que los webhook handlers (Fase 4 hardening). Skipped=true, sin tocar cursor ni publicar.</summary>
    [Fact]
    public async Task Handle_WhenAccountLockIsHeld_SkipsCleanlyWithoutTouchingCursorOrPublishing()
    {
        var account = CreateActiveAccount("office@gmail.com", ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        cursorRepository.Cursors.Add(
            TaxVision.Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "history-5", Now).Value
        );

        var sharedLock = new InMemoryDistributedLock();
        var lockKey = $"connectors:webhook-sync:{account.Id:N}";
        await using var heldByWebhook = await sharedLock.AcquireAsync(
            lockKey,
            TimeSpan.FromMinutes(2),
            CancellationToken.None
        );
        Assert.True(heldByWebhook.IsAcquired); // Simulates an in-flight push-triggered sync.

        var bus = new FakeMessageBus();

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
            cursorRepository,
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Gmail)),
            sharedLock,
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.Skipped);
        Assert.Equal(0, result.Value.MessagesFound);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("history-5", cursorRepository.Cursors[0].CursorValue); // Untouched.
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_WhenNoNewMessages_ReturnsSuccessWithZeroMessagesFound()
    {
        var account = CreateActiveAccount("office@outlook.com", ProviderCode.Graph);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        cursorRepository.Cursors.Add(
            TaxVision.Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "delta-link-1", Now).Value
        );

        var result = await ReconcileAccountHandler.Handle(
            new ReconcileAccountCommand(account.Id),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
            cursorRepository,
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Graph)),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.CursorWasSeeded);
        Assert.False(result.Value.Skipped);
        Assert.Equal(0, result.Value.MessagesFound);
    }
}
