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
    public async Task Handle_TwoNearSimultaneousDeliveries_SecondSkipsCleanlyWhileFirstIsInFlight()
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
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();
        var correlation = new FakeCorrelationContext();

        var enteredHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHistoryFetch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = new FakeEmailProviderClient(ProviderCode.Graph)
        {
            OnGetHistory = (_, _) =>
            {
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
    }
}
