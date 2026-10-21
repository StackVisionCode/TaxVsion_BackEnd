using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Accounts;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Watch;
using TaxVision.Connectors.Tests.OAuth;

namespace TaxVision.Connectors.Tests.Watch;

public sealed class GraphLifecycleNotificationHandlerTests
{
    private static readonly DateTime Now = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ReauthorizationRequired_RenewsTheSubscription()
    {
        // Un PATCH de renovación también reautoriza la subscription en Graph.
        var fixture = new Fixture();

        await fixture.HandleAsync(GraphLifecycleEvents.ReauthorizationRequired);

        Assert.Equal([fixture.Subscription.Id], fixture.Renewal.Renewed);
        Assert.Empty(fixture.Renewal.Recreated);
    }

    [Fact]
    public async Task SubscriptionRemoved_RecreatesTheSubscription()
    {
        // Graph ya la borró: renovarla daría 404.
        var fixture = new Fixture();

        await fixture.HandleAsync(GraphLifecycleEvents.SubscriptionRemoved);

        Assert.Equal([fixture.Subscription.Id], fixture.Renewal.Recreated);
        Assert.Empty(fixture.Renewal.Renewed);
    }

    [Fact]
    public async Task Missed_QueuesASyncThatPullsTheDeltaFromTheCursor()
    {
        var fixture = new Fixture();

        await fixture.HandleAsync(GraphLifecycleEvents.Missed);

        var queued = Assert.IsType<GraphNotificationReceived>(Assert.Single(fixture.Bus.Published));
        Assert.Equal("sub-123", queued.SubscriptionId);
        Assert.Equal("lifecycle-correlation", queued.CorrelationId);
        Assert.Empty(fixture.Renewal.Renewed);
    }

    [Fact]
    public async Task FailedSubscription_IsLeftAlone()
    {
        // Ya la dimos por perdida (refresh o renovación fallaron): solo reconectar la recupera.
        var fixture = new Fixture();
        fixture.Subscription.MarkFailed();

        await fixture.HandleAsync(GraphLifecycleEvents.SubscriptionRemoved);

        fixture.AssertNothingHappened();
    }

    [Fact]
    public async Task UnknownSubscription_IsLeftAlone()
    {
        // La subscription vieja que reemplazamos al reconectar sigue avisando hasta vencer.
        var fixture = new Fixture();

        await fixture.HandleAsync(GraphLifecycleEvents.ReauthorizationRequired, subscriptionRef: "old-sub");

        fixture.AssertNothingHappened();
    }

    [Theory]
    [InlineData(TenantEmailAccountStatus.Error)]
    [InlineData(TenantEmailAccountStatus.Disconnected)]
    public async Task AccountThatCannotSync_IsLeftAlone(TenantEmailAccountStatus status)
    {
        var fixture = new Fixture();
        if (status == TenantEmailAccountStatus.Error)
            fixture.Account.MarkError(Now);
        else
            fixture.Account.Disconnect(Now);

        await fixture.HandleAsync(GraphLifecycleEvents.ReauthorizationRequired);

        fixture.AssertNothingHappened();
    }

    [Fact]
    public async Task UnknownLifecycleEvent_DoesNothing()
    {
        var fixture = new Fixture();

        await fixture.HandleAsync("somethingNew");

        fixture.AssertNothingHappened();
    }

    private sealed class Fixture
    {
        public Fixture()
        {
            Account = TenantEmailAccount
                .Create(Guid.NewGuid(), "office@outlook.com", ProviderCode.Graph, Guid.NewGuid(), Now)
                .Value;
            Account.MarkConnected(Now);
            Account.Activate(Now);
            Subscription = ProviderWatchSubscription
                .Create(Account.Id, ProviderCode.Graph, "sub-123", null, Now.AddHours(70), Now)
                .Value;
            Accounts.Accounts.Add(Account);
            Subscriptions.Subscriptions.Add(Subscription);
        }

        public TenantEmailAccount Account { get; }
        public ProviderWatchSubscription Subscription { get; }
        public FakeTenantEmailAccountRepository Accounts { get; } = new();
        public FakeProviderWatchSubscriptionRepository Subscriptions { get; } = new();
        public RecordingWatchRenewalService Renewal { get; } = new();
        public FakeMessageBus Bus { get; } = new();

        public Task HandleAsync(string lifecycleEvent, string subscriptionRef = "sub-123") =>
            GraphLifecycleNotificationHandler.Handle(
                new GraphLifecycleNotificationReceived(subscriptionRef, lifecycleEvent, "lifecycle-correlation"),
                Subscriptions,
                Accounts,
                Renewal,
                Bus,
                new FakeCorrelationContext(),
                NullLogger.Instance,
                CancellationToken.None
            );

        public void AssertNothingHappened()
        {
            Assert.Empty(Renewal.Renewed);
            Assert.Empty(Renewal.Recreated);
            Assert.Empty(Bus.Published);
        }
    }

    private sealed class RecordingWatchRenewalService : IWatchRenewalService
    {
        public List<Guid> Renewed { get; } = [];
        public List<Guid> Recreated { get; } = [];

        public Task<Result> RenewAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            Renewed.Add(subscriptionId);
            return Task.FromResult(Result.Success());
        }

        public Task<Result> RecreateAsync(Guid subscriptionId, CancellationToken ct = default)
        {
            Recreated.Add(subscriptionId);
            return Task.FromResult(Result.Success());
        }
    }
}
