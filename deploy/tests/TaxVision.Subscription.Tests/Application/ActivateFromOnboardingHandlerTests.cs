using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.Commands;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Settings;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>PayFlow (Fase 16) — ActivateFromOnboardingHandler: a diferencia del trial automático
/// de TenantCreatedConsumer, la suscripción nace directo en Active; idempotente por OnboardingId.</summary>
public sealed class ActivateFromOnboardingHandlerTests
{
    [Fact]
    public async Task Activates_the_subscription_and_publishes_both_events()
    {
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();
        var plan = CreatePublishedPlan("starter");

        var subscriptions = new FakeSubscriptionRepository();
        var plans = new FakePlanRepository(plan);
        var settingsRepository = new FakeSettingsRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var command = new ActivateFromOnboardingCommand(onboardingId, tenantId, plan.Id);

        var result = await ActivateFromOnboardingHandler.Handle(
            command,
            subscriptions,
            plans,
            settingsRepository,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(subscriptions.Added);
        Assert.Equal(SubscriptionStatus.Active, subscriptions.Added!.Status);
        Assert.Equal(onboardingId, subscriptions.Added.OnboardingId);
        Assert.NotNull(settingsRepository.Added);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var activated = Assert.Single(bus.Published.OfType<SubscriptionActivatedForOnboardingIntegrationEvent>());
        Assert.Equal(tenantId, activated.TenantId);
        Assert.Equal(onboardingId, activated.OnboardingId);
        Assert.Equal(subscriptions.Added.Id, activated.CreatedSubscriptionId);

        Assert.Single(bus.Published.OfType<RecalculateEntitlementsCommand>());
    }

    [Fact]
    public async Task Is_idempotent_when_a_subscription_already_exists_for_the_onboarding()
    {
        var tenantId = Guid.NewGuid();
        var onboardingId = Guid.NewGuid();
        var plan = CreatePublishedPlan("starter");
        var existing = ActivateSubscription(tenantId, plan, onboardingId);

        var subscriptions = new FakeSubscriptionRepository { Existing = existing };
        var plans = new FakePlanRepository(plan);
        var settingsRepository = new FakeSettingsRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ActivateFromOnboardingHandler.Handle(
            new ActivateFromOnboardingCommand(onboardingId, tenantId, plan.Id),
            subscriptions,
            plans,
            settingsRepository,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Null(subscriptions.Added);
        Assert.Null(settingsRepository.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_creating_a_subscription_when_the_plan_is_missing_or_unpublished()
    {
        var subscriptions = new FakeSubscriptionRepository();
        var plans = new FakePlanRepository(existing: null);
        var settingsRepository = new FakeSettingsRepository();
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ActivateFromOnboardingHandler.Handle(
            new ActivateFromOnboardingCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
            subscriptions,
            plans,
            settingsRepository,
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.Onboarding.PlanNotFound", result.Error.Code);
        Assert.Null(subscriptions.Added);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static SubscriptionPlan CreatePublishedPlan(string code)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, versionNumber: 1, trialDaysDefault: 14, [BillingCycle.Monthly])
            .Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        var publishResult = plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        if (publishResult.IsFailure)
            throw new InvalidOperationException("Test setup failure: could not publish the plan version.");

        return plan;
    }

    private static TenantSubscription ActivateSubscription(Guid tenantId, SubscriptionPlan plan, Guid onboardingId)
    {
        var nowUtc = DateTime.UtcNow;
        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                periodStartUtc: nowUtc,
                periodEndUtc: nowUtc.AddMonths(1),
                actorUserId: Guid.Empty,
                nowUtc,
                onboardingId
            )
            .Value;
    }

    private sealed class FakeSubscriptionRepository : ISubscriptionRepository
    {
        public TenantSubscription? Existing { get; set; }
        public TenantSubscription? Added { get; private set; }

        public Task<TenantSubscription?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(TenantSubscription subscription, CancellationToken ct = default)
        {
            Added = subscription;
            return Task.CompletedTask;
        }

        public Task<TenantSubscription?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult(Existing is not null && Existing.OnboardingId == onboardingId ? Existing : null);

        public Task<IReadOnlyList<TenantSubscription>> GetDueForRenewalAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetExpiredTrialsAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetPastGracePeriodAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetSuspendedBeforeAsync(
            DateTime cutoffUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetCancelledPastPeriodEndAsync(
            DateTime nowUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<TenantSubscription>> GetRenewingBetweenAsync(
            DateTime fromUtc,
            DateTime toUtc,
            int batchSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<(IReadOnlyList<TenantSubscription> Items, int TotalCount)> GetPastDueAsync(
            int page,
            int pageSize,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakePlanRepository(SubscriptionPlan? existing) : IPlanRepository
    {
        public Task<IReadOnlyList<SubscriptionPlan>> GetPublishedAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByCodeAsync(string code, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionPlan?> GetByIdAsync(Guid planId, CancellationToken ct = default) =>
            Task.FromResult(existing is not null && existing.Id == planId ? existing : null);
    }

    private sealed class FakeSettingsRepository : ISubscriptionTenantSettingsRepository
    {
        public SubscriptionTenantSettings? Added { get; private set; }

        public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<SubscriptionTenantSettings?>(null);

        public Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default)
        {
            Added = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int SaveChangesCallCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveChangesCallCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }

    /// <summary>Fake mínimo de IMessageBus — captura lo publicado y el TenantId sellado por el
    /// handler (a diferencia de otros services, acá sí necesitamos leerlo de vuelta).</summary>
    private sealed class FakeMessageBus : IMessageBus
    {
        public List<object> Published { get; } = [];
        public string? TenantId { get; set; }

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotSupportedException();

        public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
            throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotSupportedException();

        public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
            throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotSupportedException();

        public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotSupportedException();

        public Task InvokeForTenantAsync(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeForTenantAsync<T>(
            string tenantId,
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotSupportedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public Task<T> InvokeAsync<T>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();

        public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default
        ) => throw new NotSupportedException();
    }
}
