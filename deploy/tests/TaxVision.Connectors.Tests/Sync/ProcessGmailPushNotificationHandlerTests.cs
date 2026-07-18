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

public class ProcessGmailPushNotificationHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    [Fact]
    public async Task Handle_WithNoExistingCursor_SeedsFromWatchSubscriptionAndPublishesEvents()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var watch = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Gmail, "watch-history-1", "topic", Now.AddDays(7), Now)
            .Value;
        subscriptionRepository.Subscriptions.Add(watch);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg1"], "history-2", false),
        };
        var factory = new FakeEmailProviderClientFactory(client);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();
        correlation.Set("corr-1");

        var result = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("office@gmail.com", "push-history-1"),
            accountRepository,
            subscriptionRepository,
            cursorRepository,
            factory,
            new InMemoryDistributedLock(),
            unitOfWork,
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("watch-history-1", client.ReceivedCursors[0]);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("history-2", cursorRepository.Cursors[0].CursorValue);
        var published = Assert.Single(bus.Published);
        var evt = Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(published);
        Assert.Equal(account.Id, evt.AccountId);
        Assert.Equal("corr-1", evt.CorrelationId);
    }

    [Fact]
    public async Task Handle_WithExistingCursor_UsesItAsSinceCursor()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var existingCursor = TaxVision
            .Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "history-5", Now)
            .Value;
        cursorRepository.Cursors.Add(existingCursor);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail);
        var factory = new FakeEmailProviderClientFactory(client);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var result = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("office@gmail.com", "push-history-99"),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
            cursorRepository,
            factory,
            new InMemoryDistributedLock(),
            unitOfWork,
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("history-5", client.ReceivedCursors[0]);
    }

    [Fact]
    public async Task Handle_WithUnknownEmailAddress_ReturnsFailure()
    {
        var result = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("unknown@gmail.com", "history-1"),
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
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            ThrowOnGetHistory = new EmailProviderException("Gmail API returned HTTP 400."),
        };

        var result = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("office@gmail.com", "history-1"),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
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

    [Fact]
    public async Task Handle_WhenOneMessageFetchFails_SkipsItAndPublishesTheRest()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg-bad", "msg-good"], "history-2", false),
        };
        client.ThrowOnGetMessageFor.Add("msg-bad");
        var bus = new FakeMessageBus();

        var result = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("office@gmail.com", "history-1"),
            accountRepository,
            new FakeProviderWatchSubscriptionRepository(),
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var published = Assert.Single(bus.Published);
        var evt = Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(published);
        Assert.Equal("msg-good", evt.ProviderMessageId);
    }

    /// <summary>
    /// Fase 4 (hardening): prueba real de la carrera que el IDistributedLock por-cuenta previene.
    /// Gmail Pub/Sub reentrega — simula dos deliveries de la MISMA notificación llegando casi
    /// simultáneamente. La primera se deja "en vuelo" (bloqueada a mitad de GetHistoryAsync, con el
    /// lock ya tomado) mientras la segunda llega y debe encontrar el lock ocupado y devolver
    /// Result.Success sin tocar el cursor ni publicar nada — no reintenta, no falla, no compite por
    /// escribir el cursor. Solo cuando la primera termina se ve el cursor avanzado y el evento
    /// publicado exactamente una vez.
    /// </summary>
    [Fact]
    public async Task Handle_TwoNearSimultaneousDeliveries_SecondSkipsCleanlyWhileFirstIsInFlight()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var cursorRepository = new FakeProviderSyncCursorRepository();
        var existingCursor = TaxVision
            .Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "history-5", Now)
            .Value;
        cursorRepository.Cursors.Add(existingCursor);

        var sharedLock = new InMemoryDistributedLock();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();

        var enteredHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeEmailProviderClient(ProviderCode.Gmail)
        {
            OnGetHistory = (_, _) =>
            {
                enteredHistoryFetch.TrySetResult();
                releaseHistoryFetch.Task.GetAwaiter().GetResult();
                return new HistoryPage(["msg1"], "history-2", false);
            },
        };
        var factory = new FakeEmailProviderClientFactory(client);

        // Delivery A: acquires the account lock and blocks mid-sync (simulates a slow provider call).
        var firstDelivery = Task.Run(() =>
            ProcessGmailPushNotificationHandler.Handle(
                new ProcessGmailPushNotificationCommand("office@gmail.com", "push-history-1"),
                accountRepository,
                subscriptionRepository,
                cursorRepository,
                factory,
                sharedLock,
                unitOfWork,
                bus,
                correlation,
                NullLogger.Instance,
                CancellationToken.None
            )
        );

        await enteredHistoryFetch.Task; // A has the lock and is mid-sync.

        // Delivery B: the redelivery of the SAME push notification, arriving while A is still in flight.
        var secondResult = await ProcessGmailPushNotificationHandler.Handle(
            new ProcessGmailPushNotificationCommand("office@gmail.com", "push-history-1"),
            accountRepository,
            subscriptionRepository,
            cursorRepository,
            factory,
            sharedLock,
            unitOfWork,
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(secondResult.IsSuccess);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("history-5", cursorRepository.Cursors[0].CursorValue); // Untouched by B.
        Assert.Empty(bus.Published); // B published nothing.

        releaseHistoryFetch.TrySetResult();
        var firstResult = await firstDelivery;

        Assert.True(firstResult.IsSuccess);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("history-2", cursorRepository.Cursors[0].CursorValue); // Advanced by A only.
        var published = Assert.Single(bus.Published); // Published exactly once, by A.
        Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(published);
    }
}
