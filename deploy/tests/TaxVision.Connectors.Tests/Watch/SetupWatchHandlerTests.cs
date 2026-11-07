using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Watch;

public class SetupWatchHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private static TenantEmailAccount CreateDraftAccount(ProviderCode providerCode) =>
        TenantEmailAccount.Create(TenantId, "office@example.com", providerCode, UserId, Now).Value;

    [Fact]
    public async Task Handle_WithDraftGmailAccount_SetsUpWatchAndActivates()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var watchClient = new FakeWatchProviderClient(ProviderCode.Gmail);
        var factory = new FakeWatchProviderClientFactory(watchClient);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
        Assert.Single(subscriptionRepository.Subscriptions);
        Assert.Equal(account.Id, subscriptionRepository.Subscriptions[0].AccountId);
    }

    [Fact]
    public async Task Handle_WhenActivationSucceeds_PublishesAccountConnectedEvent()
    {
        // Regresión del 403 al responder: el reauth activaba la cuenta en Connectors pero NO publicaba
        // ConnectorsTenantEmailAccountConnected, así que Postmaster nunca proyectaba la cuenta y rechazaba
        // el envío. La publicación vive ahora en WatchActivationService (punto único: connect + reauth).
        var account = CreateDraftAccount(ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail));
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, bus),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var published = Assert.Single(bus.Published.OfType<ConnectorsTenantEmailAccountConnectedIntegrationEvent>());
        Assert.Equal(account.Id, published.AccountId);
        Assert.Equal(TenantId, published.TenantId);
        Assert.Equal(account.EmailAddress, published.EmailAddress);
    }

    [Fact]
    public async Task Handle_WhenProviderThrows_DoesNotPublishAccountConnectedEvent()
    {
        // Si el watch falla la cuenta queda Connected (no Active) y NO debe anunciarse como enviable.
        var account = CreateDraftAccount(ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var watchClient = new FakeWatchProviderClient(ProviderCode.Gmail)
        {
            ThrowOnSetup = new WatchProviderException("Gmail watch request returned HTTP 500."),
        };
        var factory = new FakeWatchProviderClientFactory(watchClient);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, bus),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Empty(bus.Published.OfType<ConnectorsTenantEmailAccountConnectedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithImapAccount_ActivatesWithoutCreatingSubscription()
    {
        var account = CreateDraftAccount(ProviderCode.Imap);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(client: null);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
        Assert.Empty(subscriptionRepository.Subscriptions);
    }

    [Fact]
    public async Task Handle_WithWrongTenant_ReturnsForbidden()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail));
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(Guid.NewGuid(), account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SetupWatchHandler.Forbidden", result.Error.Code);
    }

    // Hoy cada clic en reauth pasaba la cuenta a Connected y disparaba un refresh que publicaba refresh_failed.
    [Fact]
    public async Task Reauth_ConnectionExpired_ReturnsReconnectRequiredAndPublishesNothing()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        account.MarkConnected(Now);
        account.Activate(Now);
        account.MarkError(
            new TenantEmailAccountErrorCause(TenantEmailAccountErrorSource.OAuthRefresh, "x", "y", "c"),
            Now
        );
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var connection = OAuthConnection.Create(account.Id, ProviderCode.Gmail, "client", "scope", Now).Value;
        var protector = new FakeEncryptedSecretProtector();
        connection.AttachToken(
            OAuthToken
                .Create(connection.Id, protector.Protect("a"), protector.Protect("r"), Now.AddHours(-1), Now)
                .Value
        );
        connection.MarkExpired();
        var connectionRepository = new FakeOAuthConnectionRepository();
        connectionRepository.Connections.Add(connection);
        var bus = new FakeMessageBus();

        for (var click = 0; click < 3; click++)
        {
            var result = await SetupWatchHandler.Handle(
                new SetupWatchCommand(TenantId, account.Id),
                accountRepository,
                connectionRepository,
                new FakeProviderWatchSubscriptionRepository(),
                new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail)),
                new HandlerTransactionUnit(new FakeUnitOfWork(), bus),
                new FakeCorrelationContext(),
                CancellationToken.None
            );

            Assert.True(result.IsFailure);
            Assert.Equal("Connectors.ReconnectRequired", result.Error.Code);
        }

        Assert.Equal(TenantEmailAccountStatus.Error, account.Status);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Handle_WithAccountNotFound_ReturnsFailure()
    {
        var accountRepository = new FakeTenantEmailAccountRepository();
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail));
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, Guid.NewGuid()),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantEmailAccount.NotFound", result.Error.Code);
    }

    [Fact]
    public async Task Handle_WhenProviderThrows_ReturnsFailureAndDoesNotActivate()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var watchClient = new FakeWatchProviderClient(ProviderCode.Gmail)
        {
            ThrowOnSetup = new WatchProviderException("Gmail watch request returned HTTP 500."),
        };
        var factory = new FakeWatchProviderClientFactory(watchClient);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SetupWatchHandler.ProviderFailed", result.Error.Code);
        Assert.Equal(TenantEmailAccountStatus.Draft, account.Status);
        Assert.Empty(subscriptionRepository.Subscriptions);
    }

    [Fact]
    public async Task Handle_FromErrorStatus_ReconnectsAndActivates()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        account.MarkError(AccountErrors.RefreshRejected, Now);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail));
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
    }

    [Fact]
    public async Task Handle_WithExistingSubscription_RenewsInsteadOfCreatingNew()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        account.MarkConnected(Now);
        account.Activate(Now);
        account.MarkError(AccountErrors.RefreshRejected, Now);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);

        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var existing = TaxVision
            .Connectors.Domain.Watch.ProviderWatchSubscription.Create(
                account.Id,
                ProviderCode.Gmail,
                "old-ref",
                "topic",
                Now.AddDays(1),
                Now
            )
            .Value;
        subscriptionRepository.Subscriptions.Add(existing);

        var watchClient = new FakeWatchProviderClient(ProviderCode.Gmail)
        {
            OnSetup = _ => new WatchSetupResult("new-ref", "topic", Now.AddDays(7)),
        };
        var factory = new FakeWatchProviderClientFactory(watchClient);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            new FakeOAuthConnectionRepository(),
            subscriptionRepository,
            factory,
            new HandlerTransactionUnit(unitOfWork, new FakeMessageBus()),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(subscriptionRepository.Subscriptions);
        Assert.Equal("new-ref", subscriptionRepository.Subscriptions[0].SubscriptionRef);
    }
}
