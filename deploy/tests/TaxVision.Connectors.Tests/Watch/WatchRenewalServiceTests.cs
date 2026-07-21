using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Infrastructure.Watch;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Watch;

public class WatchRenewalServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 7, 17, 12, 0, 0, DateTimeKind.Utc);

    private sealed record Fixture(
        WatchRenewalService Service,
        FakeProviderWatchSubscriptionRepository SubscriptionRepository,
        FakeTenantEmailAccountRepository AccountRepository,
        FakeWatchProviderClient WatchClient,
        FakeUnitOfWork UnitOfWork,
        FakeMessageBus Bus,
        TenantEmailAccount Account,
        ProviderWatchSubscription Subscription
    );

    private static Fixture CreateFixture()
    {
        var account = TenantEmailAccount
            .Create(TenantId, "office@gmail.com", ProviderCode.Gmail, Guid.NewGuid(), Now, "Office")
            .Value;
        account.MarkConnected(Now);
        account.Activate(Now);

        var subscription = ProviderWatchSubscription
            .Create(account.Id, ProviderCode.Gmail, "history-1", "topic", Now.AddHours(12), Now)
            .Value;

        var accountRepository = new FakeTenantEmailAccountRepository();
        accountRepository.Accounts.Add(account);
        var subscriptionRepository = new FakeProviderWatchSubscriptionRepository();
        subscriptionRepository.Subscriptions.Add(subscription);

        var watchClient = new FakeWatchProviderClient(ProviderCode.Gmail);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();
        correlation.Set("test-correlation");

        var service = new WatchRenewalService(
            subscriptionRepository,
            accountRepository,
            new FakeWatchProviderClientFactory(watchClient),
            unitOfWork,
            bus,
            correlation,
            NullLogger<WatchRenewalService>.Instance
        );

        return new Fixture(
            service,
            subscriptionRepository,
            accountRepository,
            watchClient,
            unitOfWork,
            bus,
            account,
            subscription
        );
    }

    [Fact]
    public async Task RenewAsync_WithSuccessfulRenewal_UpdatesSubscriptionAndResetsFailures()
    {
        var fixture = CreateFixture();
        fixture.Subscription.RecordRenewalFailure();
        fixture.WatchClient.OnRenew = (_, _) => new WatchSetupResult("history-2", "topic", Now.AddDays(7));

        var result = await fixture.Service.RenewAsync(fixture.Subscription.Id);

        Assert.True(result.IsSuccess);
        Assert.Equal("history-2", fixture.Subscription.SubscriptionRef);
        Assert.Equal(0, fixture.Subscription.FailureCount);
        Assert.Equal(ProviderWatchStatus.Active, fixture.Subscription.Status);
    }

    [Fact]
    public async Task RenewAsync_WithFirstFailure_RecordsFailureWithoutMarkingAccountError()
    {
        var fixture = CreateFixture();
        fixture.WatchClient.ThrowOnRenew = new WatchProviderException("Gmail watch request returned HTTP 500.");

        var result = await fixture.Service.RenewAsync(fixture.Subscription.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("WatchRenewalService.RenewalFailed", result.Error.Code);
        Assert.Equal(1, fixture.Subscription.FailureCount);
        Assert.Equal(TenantEmailAccountStatus.Active, fixture.Account.Status);
        Assert.Empty(fixture.Bus.Published);
    }

    [Fact]
    public async Task RenewAsync_WithThirdConsecutiveFailure_MarksSubscriptionFailedAccountErrorAndPublishesEvent()
    {
        var fixture = CreateFixture();
        fixture.Subscription.RecordRenewalFailure();
        fixture.Subscription.RecordRenewalFailure();
        fixture.WatchClient.ThrowOnRenew = new WatchProviderException("Gmail watch request returned HTTP 500.");

        var result = await fixture.Service.RenewAsync(fixture.Subscription.Id);

        Assert.True(result.IsFailure);
        Assert.Equal("WatchRenewalService.SubscriptionExpired", result.Error.Code);
        Assert.Equal(3, fixture.Subscription.FailureCount);
        Assert.Equal(ProviderWatchStatus.Failed, fixture.Subscription.Status);
        Assert.Equal(TenantEmailAccountStatus.Error, fixture.Account.Status);

        var published = Assert.Single(fixture.Bus.Published);
        var expiredEvent = Assert.IsType<ConnectorsWatchExpiredIntegrationEvent>(published);
        Assert.Equal(fixture.Subscription.AccountId, expiredEvent.AccountId);
        Assert.Equal(3, expiredEvent.FailureCount);
        Assert.Equal(fixture.Account.CreatedByUserId, expiredEvent.CreatedByUserId);
    }

    [Fact]
    public async Task RenewAsync_WithSubscriptionNotFound_ReturnsFailure()
    {
        var fixture = CreateFixture();

        var result = await fixture.Service.RenewAsync(Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Equal("ProviderWatchSubscription.NotFound", result.Error.Code);
    }
}
