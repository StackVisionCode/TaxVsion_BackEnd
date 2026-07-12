using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Derecho comercial y de acceso de un tenant a la plataforma. Un registro activo por
/// tenant. Independiente de los asientos (<see cref="Seats.SubscriptionSeat"/>): renovar
/// esta suscripción no renueva seats, y viceversa. Cada transición de estado es un método
/// explícito con su propia precondición — no existe un ChangeStatus(...) genérico.
/// </summary>
public sealed class TenantSubscription : TenantEntity
{
    public Guid PlanId { get; private set; }
    public Guid PlanVersionId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public SubscriptionStatus Status { get; private set; }
    public BillingCycle BillingCycle { get; private set; }

    public DateTime CurrentPeriodStartUtc { get; private set; }
    public DateTime CurrentPeriodEndUtc { get; private set; }
    public DateTime? NextRenewalAtUtc { get; private set; }
    public DateTime? TrialEndsAtUtc { get; private set; }
    public DateTime? GracePeriodEndsAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SuspensionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    private TenantSubscription() { }

    public static Result<TenantSubscription> StartTrial(
        Guid tenantId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        int trialDays,
        Guid actorUserId,
        DateTime nowUtc)
    {
        var tenantGuard = EnsureTenant(tenantId);
        if (tenantGuard.IsFailure) return Result.Failure<TenantSubscription>(tenantGuard.Error);

        if (trialDays is < 1 or > 90)
            return Result.Failure<TenantSubscription>(new Error("Subscription.InvalidTrialDays", "Trial days must be between 1 and 90."));

        var subscription = new TenantSubscription
        {
            PlanId = plan.Id,
            PlanVersionId = planVersion.Id,
            PlanCode = plan.Code.Value,
            Status = SubscriptionStatus.Trialing,
            BillingCycle = BillingCycle.Monthly,
            CurrentPeriodStartUtc = nowUtc,
            CurrentPeriodEndUtc = nowUtc.AddDays(trialDays),
            NextRenewalAtUtc = nowUtc.AddDays(trialDays),
            TrialEndsAtUtc = nowUtc.AddDays(trialDays),
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        subscription.SetTenant(tenantId);
        return Result.Success(subscription);
    }

    public static Result<TenantSubscription> ActivateImmediately(
        Guid tenantId,
        SubscriptionPlan plan,
        SubscriptionPlanVersion planVersion,
        BillingCycle billingCycle,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc)
    {
        var tenantGuard = EnsureTenant(tenantId);
        if (tenantGuard.IsFailure) return Result.Failure<TenantSubscription>(tenantGuard.Error);

        if (periodEndUtc <= periodStartUtc)
        {
            return Result.Failure<TenantSubscription>(
                new Error("Subscription.InvalidPeriod", "Period end must be after period start."));
        }

        var subscription = new TenantSubscription
        {
            PlanId = plan.Id,
            PlanVersionId = planVersion.Id,
            PlanCode = plan.Code.Value,
            Status = SubscriptionStatus.Active,
            BillingCycle = billingCycle,
            CurrentPeriodStartUtc = periodStartUtc,
            CurrentPeriodEndUtc = periodEndUtc,
            NextRenewalAtUtc = periodEndUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        subscription.SetTenant(tenantId);
        return Result.Success(subscription);
    }

    public Result ConvertTrialToActive(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Trialing)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot convert trial from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Subscription.InvalidPeriod", "Period end must be after period start."));

        Status = SubscriptionStatus.Active;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        TrialEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireTrialWithoutConversion(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Trialing)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire trial from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ChangePlan(SubscriptionPlan newPlan, SubscriptionPlanVersion newPlanVersion, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot change plan from {Status}."));

        if (PlanId == newPlan.Id && PlanVersionId == newPlanVersion.Id)
            return Result.Success();

        PlanId = newPlan.Id;
        PlanVersionId = newPlanVersion.Id;
        PlanCode = newPlan.Code.Value;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkPastDueBecauseRenewalFailed(string failureCode, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Active)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot mark past due from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("Subscription.InvalidFailureCode", "FailureCode is required."));

        Status = SubscriptionStatus.PastDue;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromPastDue(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.PastDue)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SubscriptionStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result EnterGracePeriodAfterRetriesExhausted(DateTime graceEndsAtUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.PastDue)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot enter grace period from {Status}."));

        if (graceEndsAtUtc <= nowUtc)
            return Result.Failure(new Error("Subscription.InvalidGracePeriod", "GraceEndsAtUtc must be in the future."));

        Status = SubscriptionStatus.GracePeriod;
        GracePeriodEndsAtUtc = graceEndsAtUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromGracePeriod(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.GracePeriod)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SubscriptionStatus.Active;
        GracePeriodEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendBecauseGraceExpired(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.GracePeriod)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot suspend from {Status}."));

        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = "GraceExpired";
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendForPolicyViolation(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SubscriptionStatus.Trialing, SubscriptionStatus.Active, SubscriptionStatus.PastDue, SubscriptionStatus.GracePeriod))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot suspend from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Subscription.InvalidReason", "Reason is required."));

        Status = SubscriptionStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReactivateAfterAdminReview(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Suspended)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot reactivate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Subscription.InvalidPeriod", "Period end must be after period start."));

        Status = SubscriptionStatus.Active;
        SuspendedAtUtc = null;
        SuspensionReason = null;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterSuspensionTimeout(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Suspended)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelImmediately(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (IsOneOf(Status, SubscriptionStatus.Cancelled, SubscriptionStatus.Expired))
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Subscription.InvalidReason", "Reason is required."));

        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterCancellationPeriodEnded(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SubscriptionStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static Result EnsureTenant(Guid tenantId) =>
        tenantId == Guid.Empty
            ? Result.Failure(new Error("Subscription.InvalidTenant", "TenantId is required."))
            : Result.Success();

    private static bool IsOneOf(SubscriptionStatus value, params SubscriptionStatus[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (value == candidate)
                return true;
        }

        return false;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
