using System.Reflection;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Resume;
using TaxVision.Subscription.Application.Subscriptions.Commands.Suspend;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>
/// SUB-BB1: un aviso TenantSubscriptionStatusChanged por caso de uso. Cuenta lo publicado a mano más lo devuelto
/// por el handler del evento de dominio; si quedan los dos caminos, el aviso sale dos veces y esto falla.
/// </summary>
public sealed class SubscriptionStatusChangedPublishingTests
{
    [Fact]
    public async Task FailedRenewal_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(SubscriptionRenewalPaymentFailedConsumer),
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                FailureCode = "card_declined",
                FailureReason = "Card declined",
                WillRetry = false,
                IsAutomaticCharge = true,
            }
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Active", changed.PreviousStatus);
        Assert.Equal("Suspended", changed.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.RenewalPaymentFailed), changed.Reason);
        Assert.Equal("card_declined", changed.FailureCode);
        Assert.True(changed.NotifiedByChargeFailedEmail);
    }

    [Fact]
    public async Task FailedRenewalWithRetry_EmitsNoStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(SubscriptionRenewalPaymentFailedConsumer),
            new SubscriptionRenewalPaymentFailedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                FailureCode = "insufficient_funds",
                FailureReason = "Insufficient funds",
                WillRetry = true,
                NextRetryAtUtc = DateTime.UtcNow.AddHours(1),
            }
        );

        Assert.Empty(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
    }

    [Fact]
    public async Task RecoveredRenewal_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.BeginRenewal("k1", Guid.Empty, DateTime.UtcNow);
        subscription.MarkPastDueBecauseRenewalFailed("card_declined", Guid.Empty, DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(SubscriptionRenewalPaymentSucceededConsumer),
            new SubscriptionRenewalPaymentSucceededIntegrationEvent
            {
                TenantId = subscription.TenantId,
                TenantSubscriptionId = subscription.Id,
                SaaSPaymentId = Guid.NewGuid(),
                IdempotencyKey = "k1",
                ExternalPaymentReference = "pi_123",
                PaidAtUtc = DateTime.UtcNow,
            }
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("PastDue", changed.PreviousStatus);
        Assert.Equal("Active", changed.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.PaymentRecovered), changed.Reason);
    }

    [Fact]
    public async Task ScheduleCancellation_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        var actor = Guid.NewGuid();
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(CancelSubscriptionHandler),
            new CancelSubscriptionCommand(subscription.TenantId, "too expensive", actor)
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal(nameof(SubscriptionChangeReason.CancellationScheduled), changed.Reason);
        Assert.Equal(subscription.CurrentPeriodEndUtc, changed.AccessEndsAtUtc);
        Assert.Equal(actor, changed.ActorUserId);
    }

    [Fact]
    public async Task ResumeCancellation_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(ResumeSubscriptionHandler),
            new ResumeSubscriptionCommand(subscription.TenantId, Guid.NewGuid())
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal(nameof(SubscriptionChangeReason.CancellationResumed), changed.Reason);
        Assert.Null(changed.AccessEndsAtUtc);
    }

    [Fact]
    public async Task AdminSuspend_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(SuspendSubscriptionHandler),
            new SuspendSubscriptionCommand(subscription.TenantId, "abuse", Guid.NewGuid())
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Suspended", changed.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.AdminSuspended), changed.Reason);
    }

    [Fact]
    public async Task AdminReactivate_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.SuspendForPolicyViolation("abuse", Guid.Empty, DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan);

        await fixture.RunAsync(
            typeof(ReactivateSubscriptionHandler),
            new ReactivateSubscriptionCommand(subscription.TenantId, Guid.NewGuid())
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Suspended", changed.PreviousStatus);
        Assert.Equal("Active", changed.Status);
        Assert.Equal(nameof(SubscriptionChangeReason.AdminReactivated), changed.Reason);
    }

    [Fact]
    public async Task RenewalCheckoutPaid_EmitsExactlyOneStatusChanged()
    {
        var subscription = ActiveSubscription(out var plan);
        subscription.SuspendForPolicyViolation("unpaid", Guid.Empty, DateTime.UtcNow);
        var intent = SubscriptionRenewalIntent
            .Create(subscription.TenantId, 4900, "USD", BillingCycle.Monthly, Guid.NewGuid(), DateTime.UtcNow)
            .Value;
        intent.AttachCheckout(Guid.NewGuid(), "https://pay/x", DateTime.UtcNow.AddHours(24), DateTime.UtcNow);
        var fixture = new Fixture(subscription, plan, new FakeRenewalCheckoutIntentRepository(intent));

        await fixture.RunAsync(
            typeof(SubscriptionRenewalCheckoutPaidConsumer),
            new SubscriptionRenewalCheckoutPaidIntegrationEvent
            {
                TenantId = subscription.TenantId,
                RenewalIntentId = intent.Id,
                SaaSPaymentId = Guid.NewGuid(),
                AmountPaidCents = 4900,
                Currency = "USD",
                PaidAtUtc = DateTime.UtcNow,
                ProviderPaymentReference = "pi_123",
            }
        );

        var changed = Assert.Single(fixture.Emitted().OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal("Suspended", changed.PreviousStatus);
        Assert.Equal(nameof(SubscriptionChangeReason.SelfServiceRenewed), changed.Reason);
        Assert.Equal(intent.RequestedByUserId, changed.ActorUserId);
    }

    // Los re-anuncios sin transición (anti-entropía, recordatorio de fin de acceso) siguen igual.
    [Fact]
    public void Reannouncement_CarriesCurrentStatusAsPrevious()
    {
        var subscription = ActiveSubscription(out _);
        subscription.ScheduleCancellation("too expensive", Guid.Empty, DateTime.UtcNow);

        var notice = SubscriptionStatusChangedPublishing.Reannouncement(
            subscription,
            SubscriptionChangeReason.AccessEnding,
            "trace"
        );

        Assert.Equal("Active", notice.Status);
        Assert.Equal("Active", notice.PreviousStatus);
        Assert.Equal(nameof(SubscriptionChangeReason.AccessEnding), notice.Reason);
        Assert.Equal(subscription.CurrentPeriodEndUtc, notice.AccessEndsAtUtc);
        Assert.Equal("trace", notice.CorrelationId);
    }

    private static TenantSubscription ActiveSubscription(out SubscriptionPlan plan)
    {
        var nowUtc = DateTime.UtcNow;
        plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter plan", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        PlanTestData.EnsurePublishableFeature(version);
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc.AddDays(-10),
                nowUtc.AddDays(20),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }

    private sealed class NoCheckoutToCancel : IHostedCheckoutCancellationClient
    {
        public Task<Result> CancelAsync(Guid tenantId, Guid saaSPaymentId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    /// <summary>Corre el handler resolviendo sus parámetros por tipo, sin atarse a su firma.</summary>
    private sealed class Fixture(TenantSubscription subscription, SubscriptionPlan plan, params object[] extra)
    {
        private readonly CapturingMessageBus _bus = new();

        public IReadOnlyList<object> Emitted() => SubscriptionDomainEventReplay.Emitted(_bus, subscription);

        public async Task RunAsync(Type handler, object message)
        {
            var services = new List<object>(extra)
            {
                new FakeSubscriptionRepo(subscription),
                new FakePlanRepo(plan),
                new FakeUnitOfWork(),
                _bus,
                new FakeCorrelationContext(),
                new FakeSubscriptionAuditLogWriter(),
                new FakeSubscriptionMetrics(),
                new NoCheckoutToCancel(),
            };
            var method = handler
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == "Handle" && m.GetParameters()[0].ParameterType == message.GetType());
            var args = method
                .GetParameters()
                .Select(
                    object? (p) =>
                        p.ParameterType == message.GetType() ? message
                        : p.ParameterType == typeof(CancellationToken) ? CancellationToken.None
                        : typeof(ILogger).IsAssignableFrom(p.ParameterType)
                            ? Activator.CreateInstance(
                                typeof(NullLogger<>).MakeGenericType(p.ParameterType.GetGenericArguments()[0])
                            )
                        : services.First(s => p.ParameterType.IsInstanceOfType(s))
                )
                .ToArray();
            var outcome = method.Invoke(null, args);
            if (outcome is Task<Result> withResult)
                Assert.True((await withResult).IsSuccess);
            else
                await (Task)outcome!;
        }
    }
}
