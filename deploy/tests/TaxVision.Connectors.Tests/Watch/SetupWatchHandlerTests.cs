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
            subscriptionRepository,
            factory,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantEmailAccountStatus.Active, account.Status);
        Assert.Single(subscriptionRepository.Subscriptions);
        Assert.Equal(account.Id, subscriptionRepository.Subscriptions[0].AccountId);
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
            subscriptionRepository,
            factory,
            unitOfWork,
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
            subscriptionRepository,
            factory,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SetupWatchHandler.Forbidden", result.Error.Code);
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
            subscriptionRepository,
            factory,
            unitOfWork,
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
            subscriptionRepository,
            factory,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SetupWatchHandler.ProviderFailed", result.Error.Code);
        Assert.Equal(TenantEmailAccountStatus.Connected, account.Status);
        Assert.Empty(subscriptionRepository.Subscriptions);
    }

    [Fact]
    public async Task Handle_FromErrorStatus_ReconnectsAndActivates()
    {
        var account = CreateDraftAccount(ProviderCode.Gmail);
        account.MarkError(Now);
        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        var factory = new FakeWatchProviderClientFactory(new FakeWatchProviderClient(ProviderCode.Gmail));
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetupWatchHandler.Handle(
            new SetupWatchCommand(TenantId, account.Id),
            accountRepository,
            subscriptionRepository,
            factory,
            unitOfWork,
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
        account.MarkError(Now);
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
            subscriptionRepository,
            factory,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(subscriptionRepository.Subscriptions);
        Assert.Equal("new-ref", subscriptionRepository.Subscriptions[0].SubscriptionRef);
    }
}
