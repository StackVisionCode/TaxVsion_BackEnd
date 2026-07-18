using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantSubscriptionTests
{
    [Fact]
    public void StartTrial_uses_plan_and_requested_duration()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var before = DateTime.UtcNow;

        var result = TenantSubscription.StartTrial(Guid.NewGuid(), plan, version, trialDays: 14, Guid.Empty, before);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trialing, result.Value.Status);
        Assert.Equal("starter", result.Value.PlanCode);
        Assert.Equal(before.AddDays(14), result.Value.TrialEndsAtUtc);
    }

    [Fact]
    public void ChangePlan_from_trial_keeps_trialing_status()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), starter, starterVersion, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.ChangePlan(pro, proVersion, null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Equal("pro", subscription.PlanCode);
    }

    [Fact]
    public void SuspendForPolicyViolation_during_trial_is_allowed()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.SuspendForPolicyViolation("abuse", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Suspended, subscription.Status);
    }

    [Fact]
    public void CancelImmediately_on_an_already_cancelled_subscription_fails()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        subscription.CancelImmediately("tenant requested", Guid.Empty, DateTime.UtcNow);

        var result = subscription.CancelImmediately("tenant requested again", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void ReactivateAfterAdminReview_requires_suspended_status()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.ReactivateAfterAdminReview(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void RequestUpgrade_does_not_switch_the_plan_yet_and_creates_an_awaiting_payment_request()
    {
        // Regression: un upgrade nunca debe otorgar el plan nuevo antes de que PaymentApp
        // confirme el cobro del precio completo.
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = subscription.RequestUpgrade(
            pro,
            proVersion,
            null,
            chargeAmountCents: 8000,
            "usd",
            "plan-change-token-1",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("starter", subscription.PlanCode);
        var request = Assert.Single(subscription.PlanChangeRequests);
        Assert.Equal(PlanChangeRequestStatus.AwaitingPayment, request.Status);
        Assert.Equal(8000, request.ChargeAmountCents);
        Assert.Equal("usd", request.ChargeCurrency);
        Assert.Equal("plan-change-token-1", request.PaymentIdempotencyKey);
    }

    [Fact]
    public void RequestUpgrade_rejects_a_second_request_while_one_is_awaiting_payment()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var (enterprise, enterpriseVersion) = CreatePublishedPlan("enterprise");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestUpgrade(
            pro,
            proVersion,
            null,
            chargeAmountCents: 8000,
            "usd",
            "plan-change-token-1",
            Guid.Empty,
            DateTime.UtcNow
        );

        var result = subscription.RequestUpgrade(
            enterprise,
            enterpriseVersion,
            null,
            chargeAmountCents: 20000,
            "usd",
            "plan-change-token-2",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlanChangeRequest.PaymentInProgress", result.Error.Code);
        Assert.Single(subscription.PlanChangeRequests);
    }

    [Fact]
    public void CompleteUpgradeCharge_applies_the_plan_and_resets_the_billing_cycle_from_now()
    {
        // La regla de negocio es explícita: un upgrade exitoso reinicia el ciclo desde hoy —
        // no es una continuación del período anterior.
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var originalPeriodStart = DateTime.UtcNow.AddDays(-20);
        var originalPeriodEnd = originalPeriodStart.AddMonths(1);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                originalPeriodStart,
                originalPeriodEnd,
                Guid.Empty,
                originalPeriodStart
            )
            .Value;
        subscription.RequestUpgrade(
            pro,
            proVersion,
            null,
            chargeAmountCents: 8000,
            "usd",
            "plan-change-token-1",
            Guid.Empty,
            DateTime.UtcNow
        );
        var request = Assert.Single(subscription.PlanChangeRequests);
        var saaSPaymentId = Guid.NewGuid();
        var paidAtUtc = DateTime.UtcNow;

        var result = subscription.CompleteUpgradeCharge(
            request.Id,
            pro,
            proVersion,
            saaSPaymentId,
            Guid.Empty,
            paidAtUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("pro", subscription.PlanCode);
        Assert.Equal(PlanChangeRequestStatus.Applied, request.Status);
        Assert.Equal(saaSPaymentId, request.SaaSPaymentId);
        Assert.Equal(paidAtUtc, subscription.CurrentPeriodStartUtc);
        Assert.Equal(BillingCycle.Monthly.CalculateNext(paidAtUtc), subscription.CurrentPeriodEndUtc);
        Assert.Equal(subscription.CurrentPeriodEndUtc, subscription.NextRenewalAtUtc);
    }

    [Fact]
    public void FailUpgradeCharge_leaves_the_plan_unchanged()
    {
        // Regression: si el cobro del upgrade falla, no hay nada que "revertir" — ChangePlan
        // nunca se llamó para este request.
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestUpgrade(
            pro,
            proVersion,
            null,
            chargeAmountCents: 8000,
            "usd",
            "plan-change-token-1",
            Guid.Empty,
            DateTime.UtcNow
        );
        var request = Assert.Single(subscription.PlanChangeRequests);
        var saaSPaymentId = Guid.NewGuid();

        var result = subscription.FailUpgradeCharge(request.Id, saaSPaymentId, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("starter", subscription.PlanCode);
        Assert.Equal(PlanChangeRequestStatus.PaymentFailed, request.Status);
        Assert.Equal(saaSPaymentId, request.SaaSPaymentId);
    }

    [Fact]
    public void RequestDowngrade_schedules_without_switching_plan_or_charging_anything()
    {
        // Downgrade: nunca cobra, nunca prorratea, nunca genera crédito — se agenda para el
        // fin del período actual (equivalente a proration_behavior=none).
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                pro,
                proVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = subscription.RequestDowngrade(starter, starterVersion, null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("pro", subscription.PlanCode);
        var pending = Assert.Single(subscription.PendingDowngrades);
        Assert.Equal(PendingDowngradeStatus.Scheduled, pending.Status);
        Assert.Equal(subscription.CurrentPeriodEndUtc, pending.EffectiveAtUtc);
        Assert.Empty(subscription.PlanChangeRequests);
    }

    [Fact]
    public void CancelPendingDowngrade_leaves_plan_unchanged_and_marks_cancelled()
    {
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                pro,
                proVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestDowngrade(starter, starterVersion, null, Guid.Empty, DateTime.UtcNow);
        var pending = Assert.Single(subscription.PendingDowngrades);

        var result = subscription.CancelPendingDowngrade(pending.Id, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("pro", subscription.PlanCode);
        Assert.Equal(PendingDowngradeStatus.Cancelled, pending.Status);
    }

    [Fact]
    public void ApplyPendingDowngrade_switches_plan_and_marks_applied()
    {
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                pro,
                proVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestDowngrade(starter, starterVersion, null, Guid.Empty, DateTime.UtcNow);
        var pending = Assert.Single(subscription.PendingDowngrades);

        var result = subscription.ApplyPendingDowngrade(
            pending.Id,
            starter,
            starterVersion,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("starter", subscription.PlanCode);
        Assert.Equal(PendingDowngradeStatus.Applied, pending.Status);
    }

    [Fact]
    public void RequestDowngrade_supersedes_a_previously_scheduled_downgrade()
    {
        var (enterprise, enterpriseVersion) = CreatePublishedPlan("enterprise");
        var (pro, proVersion) = CreatePublishedPlan("pro");
        var (starter, starterVersion) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                enterprise,
                enterpriseVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestDowngrade(pro, proVersion, null, Guid.Empty, DateTime.UtcNow);

        var result = subscription.RequestDowngrade(starter, starterVersion, null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, subscription.PendingDowngrades.Count);
        Assert.Contains(
            subscription.PendingDowngrades,
            d => d.ToPlanCode == "pro" && d.Status == PendingDowngradeStatus.Cancelled
        );
        Assert.Contains(
            subscription.PendingDowngrades,
            d => d.ToPlanCode == "starter" && d.Status == PendingDowngradeStatus.Scheduled
        );
    }

    [Fact]
    public void RequestDowngrade_with_only_billing_cycle_change_queues_a_request()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter", [BillingCycle.Monthly, BillingCycle.Yearly]);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = subscription.RequestDowngrade(
            starter,
            starterVersion,
            BillingCycle.Yearly,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingCycle.Monthly, subscription.BillingCycle);
        var pending = Assert.Single(subscription.PendingDowngrades);
        Assert.Equal(PendingDowngradeStatus.Scheduled, pending.Status);
        Assert.Equal(BillingCycle.Yearly, pending.ToBillingCycle);
    }

    [Fact]
    public void ApplyPendingDowngrade_applies_the_requested_billing_cycle_from_the_pending_request()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter", [BillingCycle.Monthly, BillingCycle.Yearly]);
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                starter,
                starterVersion,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        subscription.RequestDowngrade(starter, starterVersion, BillingCycle.Yearly, Guid.Empty, DateTime.UtcNow);
        var pending = Assert.Single(subscription.PendingDowngrades);

        var result = subscription.ApplyPendingDowngrade(
            pending.Id,
            starter,
            starterVersion,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingCycle.Yearly, subscription.BillingCycle);
        Assert.Equal(PendingDowngradeStatus.Applied, pending.Status);
    }

    [Fact]
    public void ConvertTrialToActive_moves_from_Trialing_to_Active_and_clears_TrialEndsAtUtc()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = nowUtc.AddMonths(1);

        var result = subscription.ConvertTrialToActive(nowUtc, periodEndUtc, null, Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Null(subscription.TrialEndsAtUtc);
        Assert.Equal(nowUtc, subscription.CurrentPeriodStartUtc);
        Assert.Equal(periodEndUtc, subscription.CurrentPeriodEndUtc);
        Assert.Equal(periodEndUtc, subscription.NextRenewalAtUtc);
    }

    [Fact]
    public void ConvertTrialToActive_on_an_already_Active_subscription_fails()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                DateTime.UtcNow,
                DateTime.UtcNow.AddMonths(1),
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = subscription.ConvertTrialToActive(
            DateTime.UtcNow,
            DateTime.UtcNow.AddMonths(1),
            null,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void BeginActivationCharge_schedules_a_renewal_for_the_period_already_set_by_ConvertTrialToActive()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = nowUtc.AddMonths(1);
        subscription.ConvertTrialToActive(nowUtc, periodEndUtc, null, Guid.Empty, nowUtc);

        var result = subscription.BeginActivationCharge("activation-key-1", Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        var renewal = Assert.Single(subscription.Renewals);
        Assert.Equal(nowUtc, renewal.PeriodStartUtc);
        Assert.Equal(periodEndUtc, renewal.PeriodEndUtc);
    }

    [Fact]
    public void BeginActivationCharge_requires_Active_status()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.BeginActivationCharge("activation-key-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void BeginActivationCharge_is_idempotent_by_key()
    {
        var (plan, version) = CreatePublishedPlan("starter");
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        var nowUtc = DateTime.UtcNow;
        subscription.ConvertTrialToActive(nowUtc, nowUtc.AddMonths(1), null, Guid.Empty, nowUtc);
        subscription.BeginActivationCharge("activation-key-1", Guid.Empty, nowUtc);

        var result = subscription.BeginActivationCharge("activation-key-1", Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Single(subscription.Renewals);
    }

    [Fact]
    public void ChangePlan_switching_only_billing_cycle_updates_cycle_and_keeps_plan()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter", [BillingCycle.Monthly, BillingCycle.Yearly]);
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), starter, starterVersion, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        Assert.Equal(BillingCycle.Monthly, subscription.BillingCycle);

        var result = subscription.ChangePlan(starter, starterVersion, BillingCycle.Yearly, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal("starter", subscription.PlanCode);
        Assert.Equal(BillingCycle.Yearly, subscription.BillingCycle);
    }

    [Fact]
    public void ChangePlan_rejects_a_billing_cycle_the_plan_version_does_not_support()
    {
        var (starter, starterVersion) = CreatePublishedPlan("starter", [BillingCycle.Monthly]);
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), starter, starterVersion, 14, Guid.Empty, DateTime.UtcNow)
            .Value;

        var result = subscription.ChangePlan(starter, starterVersion, BillingCycle.Yearly, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Subscription.UnsupportedBillingCycle", result.Error.Code);
        Assert.Equal(BillingCycle.Monthly, subscription.BillingCycle);
    }

    [Fact]
    public void ConvertTrialToActive_with_requested_billing_cycle_switches_the_cycle()
    {
        var (plan, version) = CreatePublishedPlan("starter", [BillingCycle.Monthly, BillingCycle.Yearly]);
        var subscription = TenantSubscription
            .StartTrial(Guid.NewGuid(), plan, version, 14, Guid.Empty, DateTime.UtcNow)
            .Value;
        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = nowUtc.AddYears(1);

        var result = subscription.ConvertTrialToActive(nowUtc, periodEndUtc, BillingCycle.Yearly, Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(BillingCycle.Yearly, subscription.BillingCycle);
    }

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(string code) =>
        CreatePublishedPlan(code, [BillingCycle.Monthly]);

    private static (SubscriptionPlan Plan, SubscriptionPlanVersion Version) CreatePublishedPlan(
        string code,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles
    )
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create(code).Value, code, $"{code} plan", PlanTier.Standard, Guid.Empty, DateTime.UtcNow)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, trialDaysDefault: 14, supportedBillingCycles).Value;
        plan.AddVersion(version, Guid.Empty, DateTime.UtcNow);
        plan.PublishVersion(version.Id, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        return (plan, version);
    }
}
