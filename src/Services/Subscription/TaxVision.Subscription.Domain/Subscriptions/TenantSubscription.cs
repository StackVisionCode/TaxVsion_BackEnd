using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Domain.Subscriptions;

public enum SubscriptionStatus
{
    Trial,
    Active,
    Suspended,
    Cancelled,
}

/// <summary>Suscripción de un tenant. Un registro por tenant (índice único en TenantId).</summary>
public sealed class TenantSubscription : BaseEntity
{
    private TenantSubscription() { }

    public Guid TenantId { get; private set; }
    public Guid PlanId { get; private set; }
    public string PlanCode { get; private set; } = default!;
    public SubscriptionStatus Status { get; private set; }

    /// <summary>Asientos comprados por encima de los incluidos en el plan.</summary>
    public int ExtraSeats { get; private set; }

    public DateTime? TrialEndsAtUtc { get; private set; }
    public DateTime CurrentPeriodStartUtc { get; private set; }
    public DateTime CurrentPeriodEndUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public string? SuspensionReason { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static Result<TenantSubscription> StartTrial(Guid tenantId, Plan plan, int trialDays)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantSubscription>(new Error("Subscription.Tenant", "Tenant is required."));

        if (trialDays is < 1 or > 90)
            return Result.Failure<TenantSubscription>(
                new Error("Subscription.Trial", "Trial days must be between 1 and 90.")
            );

        var now = DateTime.UtcNow;
        return Result.Success(
            new TenantSubscription
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                PlanId = plan.Id,
                PlanCode = plan.Code,
                Status = SubscriptionStatus.Trial,
                TrialEndsAtUtc = now.AddDays(trialDays),
                CurrentPeriodStartUtc = now,
                CurrentPeriodEndUtc = now.AddDays(trialDays),
                CreatedAtUtc = now,
                UpdatedAtUtc = now,
            }
        );
    }

    public Result ChangePlan(Plan newPlan)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.Cancelled", "A cancelled subscription cannot change plan."));

        PlanId = newPlan.Id;
        PlanCode = newPlan.Code;
        if (Status == SubscriptionStatus.Trial)
        {
            Status = SubscriptionStatus.Active;
            TrialEndsAtUtc = null;
        }

        Touch();
        return Result.Success();
    }

    public Result AddSeats(int additionalSeats)
    {
        if (additionalSeats is < 1 or > 500)
            return Result.Failure(new Error("Subscription.Seats", "Additional seats must be between 1 and 500."));

        if (Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Suspended)
            return Result.Failure(new Error("Subscription.Inactive", "Subscription is not active."));

        ExtraSeats += additionalSeats;
        Touch();
        return Result.Success();
    }

    public Result Suspend(string reason)
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Failure(new Error("Subscription.Cancelled", "Subscription is already cancelled."));

        Status = SubscriptionStatus.Suspended;
        SuspensionReason = reason is { Length: > 128 } ? reason[..128] : reason;
        Touch();
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != SubscriptionStatus.Suspended)
            return Result.Failure(
                new Error("Subscription.NotSuspended", "Only suspended subscriptions can be reactivated.")
            );

        Status = SubscriptionStatus.Active;
        SuspensionReason = null;
        RenewPeriod();
        return Result.Success();
    }

    public Result Cancel()
    {
        if (Status == SubscriptionStatus.Cancelled)
            return Result.Success();

        Status = SubscriptionStatus.Cancelled;
        CancelledAtUtc = DateTime.UtcNow;
        Touch();
        return Result.Success();
    }

    public void RenewPeriod()
    {
        var now = DateTime.UtcNow;
        CurrentPeriodStartUtc = now;
        CurrentPeriodEndUtc = now.AddMonths(1);
        Touch();
    }

    public int EffectiveMaxUsers(Plan plan) => plan.MaxUsers + ExtraSeats;

    private void Touch() => UpdatedAtUtc = DateTime.UtcNow;
}
