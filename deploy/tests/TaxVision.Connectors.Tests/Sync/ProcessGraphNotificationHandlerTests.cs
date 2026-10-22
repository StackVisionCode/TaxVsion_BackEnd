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

public class ProcessGraphNotificationHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateActiveAccount()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@outlook.com", ProviderCode.Graph, Guid.NewGuid(), Now)
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);
        return account;
    }

    [Fact]
    public async Task Handle_WithKnownSubscription_PublishesEventsAndCreatesNullSeededCursor()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now)
            .Value;
        subscriptionRepository.Subscriptions.Add(subscription);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg1"], "delta-link-1", false),
        };
        var bus = new FakeMessageBus();

        var result = await ProcessGraphNotificationHandler.Handle(
            new ProcessGraphNotificationCommand("sub-123"),
            subscriptionRepository,
            accountRepository,
            cursorRepository,
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(client.ReceivedCursors[0]);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("delta-link-1", cursorRepository.Cursors[0].CursorValue);
        var published = Assert.Single(bus.Published);
        Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(published);
    }

    [Fact]
    public async Task Handle_WithUnknownSubscriptionId_ReturnsFailure()
    {
        var result = await ProcessGraphNotificationHandler.Handle(
            new ProcessGraphNotificationCommand("unknown-sub"),
            new FakeProviderWatchSubscriptionRepository(),
            new FakeTenantEmailAccountRepository(),
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Graph)),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WithExistingCursor_ReusesItInsteadOfCreatingNew()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now)
            .Value;
        subscriptionRepository.Subscriptions.Add(subscription);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var existing = TaxVision
            .Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "delta-link-old", Now)
            .Value;
        cursorRepository.Cursors.Add(existing);

        var client = new FakeEmailProviderClient(ProviderCode.Graph);

        var result = await ProcessGraphNotificationHandler.Handle(
            new ProcessGraphNotificationCommand("sub-123"),
            subscriptionRepository,
            accountRepository,
            cursorRepository,
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("delta-link-old", client.ReceivedCursors[0]);
    }

    /// <summary>Fase 4 (hardening): mismo escenario y razonamiento que su equivalente en ProcessGmailPushNotificationHandlerTests — Graph subscriptions también reentregan.</summary>
    [Fact]
    public async Task Handle_TwoNearSimultaneousDeliveries_SecondMarksPendingAndFirstRunsOneMorePass()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now)
            .Value;
        subscriptionRepository.Subscriptions.Add(subscription);

        var cursorRepository = new FakeProviderSyncCursorRepository();
        var existingCursor = TaxVision
            .Connectors.Domain.Sync.ProviderSyncCursor.Create(account.Id, "delta-link-5", Now)
            .Value;
        cursorRepository.Cursors.Add(existingCursor);

        var sharedLock = new InMemoryDistributedLock();

        var sharedPendingFlag = new InMemorySyncPendingFlag();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();

        var enteredHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            // Primera pasada trae msg1; la segunda (desde el cursor nuevo) ya no trae nada.
            OnGetHistory = (_, cursor) =>
            {
                if (cursor != "delta-link-5")
                    return new HistoryPage([], "delta-link-6", false);
                enteredHistoryFetch.TrySetResult();
                releaseHistoryFetch.Task.GetAwaiter().GetResult();
                return new HistoryPage(["msg1"], "delta-link-6", false);
            },
        };
        var factory = new FakeEmailProviderClientFactory(client);

        // Delivery A: acquires the account lock and blocks mid-sync (simulates a slow Graph call).
        var firstDelivery = Task.Run(() =>
            ProcessGraphNotificationHandler.Handle(
                new ProcessGraphNotificationCommand("sub-123"),
                subscriptionRepository,
                accountRepository,
                cursorRepository,
                factory,
                sharedLock,
                sharedPendingFlag,
                unitOfWork,
                bus,
                correlation,
                NullLogger.Instance,
                CancellationToken.None
            )
        );

        await enteredHistoryFetch.Task; // A has the lock and is mid-sync.

        // Delivery B: the redelivery of the SAME notification, arriving while A is still in flight.
        var secondResult = await ProcessGraphNotificationHandler.Handle(
            new ProcessGraphNotificationCommand("sub-123"),
            subscriptionRepository,
            accountRepository,
            cursorRepository,
            factory,
            sharedLock,
            sharedPendingFlag,
            unitOfWork,
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        Assert.True(secondResult.IsSuccess);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("delta-link-5", cursorRepository.Cursors[0].CursorValue); // Untouched by B.
        Assert.Empty(bus.Published); // B published nothing.

        releaseHistoryFetch.TrySetResult();
        var firstResult = await firstDelivery;

        Assert.True(firstResult.IsSuccess);
        Assert.Single(cursorRepository.Cursors);
        Assert.Equal("delta-link-6", cursorRepository.Cursors[0].CursorValue); // Advanced by A only.
        var published = Assert.Single(bus.Published); // Published exactly once, by A.
        Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(published);
        // B dejó la marca y A hizo una pasada más desde el cursor avanzado.
        Assert.Equal(1, sharedPendingFlag.MarkCount);
        Assert.Equal(["delta-link-5", "delta-link-6"], client.ReceivedCursors);
    }

    [Fact]
    public async Task Handle_WhenSubscriptionIsFailed_IgnoresItWithoutLoadingTheAccount()
    {
        // La subscription vieja sigue notificando hasta vencer en Microsoft (~70 h). El repositorio
        // de cuentas vacío prueba que no se cargó: si se cargara, el resultado sería NotFound.
        var subscription = ProviderWatchSubscription
            .Create(Guid.NewGuid(), ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now)
            .Value;
        subscription.MarkFailed();
        var client = new FakeEmailProviderClient(ProviderCode.Graph);

        var result = await HandleAsync(new FakeTenantEmailAccountRepository(), subscription, client);

        Assert.True(result.IsSuccess);
        Assert.Empty(client.ReceivedCursors);
    }

    [Theory]
    [InlineData(TenantEmailAccountStatus.Error)]
    [InlineData(TenantEmailAccountStatus.Disconnected)]
    public async Task Handle_WhenAccountCannotSync_AcknowledgesWithoutCallingGraph(TenantEmailAccountStatus status)
    {
        var account = CreateActiveAccount();
        if (status == TenantEmailAccountStatus.Error)
            account.MarkError(Now);
        else
            account.Disconnect(Now);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Graph);

        var result = await HandleAsync(accountRepository, ActiveSubscriptionFor(account), client);

        Assert.True(result.IsSuccess);
        Assert.Empty(client.ReceivedCursors);
    }

    [Fact]
    public async Task Handle_WhenTheAccountNeedsReconnect_Acknowledges()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            ThrowOnGetHistory = new EmailProviderException(
                "Could not obtain a valid access token: invalid_grant",
                requiresReconnect: true
            ),
        };

        var result = await HandleAsync(accountRepository, ActiveSubscriptionFor(account), client);

        Assert.True(result.IsSuccess);
        Assert.Single(client.ReceivedCursors);
    }

    [Fact]
    public async Task Handle_WhenGraphFailsTransiently_ReportsTheFailure()
    {
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            ThrowOnGetHistory = new EmailProviderException("Graph API returned HTTP 503."),
        };

        var result = await HandleAsync(accountRepository, ActiveSubscriptionFor(account), client);

        Assert.True(result.IsFailure);
        Assert.Equal("RawMessageSync.HistoryFetchFailed", result.Error.Code);
    }

    private static ProviderWatchSubscription ActiveSubscriptionFor(TenantEmailAccount account) =>
        ProviderWatchSubscription.Create(account.Id, ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now).Value;

    private static Task<BuildingBlocks.Results.Result> HandleAsync(
        FakeTenantEmailAccountRepository accountRepository,
        ProviderWatchSubscription subscription,
        FakeEmailProviderClient client
    )
    {
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        subscriptionRepository.Subscriptions.Add(subscription);

        return ProcessGraphNotificationHandler.Handle(
            new ProcessGraphNotificationCommand(subscription.SubscriptionRef),
            subscriptionRepository,
            accountRepository,
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );
    }

    [Fact]
    public async Task QueuedNotification_RunsTheSyncUnderTheWebhookCorrelation()
    {
        // La cola no hereda el contexto ambient de la request: el id viaja en el mensaje.
        var account = CreateActiveAccount();
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        subscriptionRepository.Subscriptions.Add(ActiveSubscriptionFor(account));
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            OnGetHistory = (_, _) => new HistoryPage(["msg1"], "delta-link-1", false),
        };
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        await GraphNotificationReceivedHandler.Handle(
            new GraphNotificationReceived("sub-123", "webhook-correlation"),
            subscriptionRepository,
            accountRepository,
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(client),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            bus,
            correlation,
            NullLogger.Instance,
            CancellationToken.None
        );

        var published = Assert.IsType<ConnectorsRawMessageReceivedIntegrationEvent>(Assert.Single(bus.Published));
        Assert.Equal("webhook-correlation", published.CorrelationId);
        Assert.Equal(string.Empty, correlation.CorrelationId);
    }

    [Fact]
    public async Task QueuedNotification_ForAReplacedSubscription_DoesNotThrow()
    {
        await GraphNotificationReceivedHandler.Handle(
            new GraphNotificationReceived("old-sub", "webhook-correlation"),
            new FakeProviderWatchSubscriptionRepository(),
            new FakeTenantEmailAccountRepository(),
            new FakeProviderSyncCursorRepository(),
            new FakeEmailProviderClientFactory(new FakeEmailProviderClient(ProviderCode.Graph)),
            new InMemoryDistributedLock(),
            new InMemorySyncPendingFlag(),
            new FakeUnitOfWork(),
            new FakeMessageBus(),
            new FakeCorrelationContext(),
            NullLogger.Instance,
            CancellationToken.None
        );
    }
}
