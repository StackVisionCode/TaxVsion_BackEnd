using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>
/// Instancia viva de un add-on comprado por un tenant. Independiente de
/// <see cref="Subscriptions.TenantSubscription"/> y de los seats: tiene su propio ciclo
/// comercial y su propia renovación (renovación llega en una fase posterior).
/// </summary>
public sealed class TenantAddOn : TenantEntity
{
    public Guid AddOnDefinitionId { get; private set; }
    public string AddOnCode { get; private set; } = default!;
    public AddOnStatus Status { get; private set; }
    public int Quantity { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public DateTime CurrentPeriodStartUtc { get; private set; }
    public DateTime CurrentPeriodEndUtc { get; private set; }
    public DateTime? NextRenewalAtUtc { get; private set; }
    public DateTime? GracePeriodEndsAtUtc { get; private set; }
    public bool AutoRenew { get; private set; }
    public Money UnitPrice { get; private set; } = null!;
    public DateTime PurchasedAtUtc { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SuspensionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    private TenantAddOn() { }

    public static Result<TenantAddOn> Purchase(
        Guid tenantId,
        AddOnDefinition definition,
        int quantity,
        Money unitPrice,
        BillingCycle billingCycle,
        bool autoRenew,
        Guid actorUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantAddOn>(new Error("AddOn.InvalidTenant", "TenantId is required."));

        if (quantity < 1)
            return Result.Failure<TenantAddOn>(new Error("AddOn.InvalidQuantity", "Quantity must be at least 1."));

        if (quantity > 1 && !definition.AllowMultipleInstances)
            return Result.Failure<TenantAddOn>(new Error("AddOn.MultipleInstancesNotAllowed", "This add-on does not allow more than one instance."));

        var periodEndUtc = billingCycle.CalculateNext(nowUtc);
        var addOn = new TenantAddOn
        {
            AddOnDefinitionId = definition.Id,
            AddOnCode = definition.Code.Value,
            Status = AddOnStatus.Active,
            Quantity = quantity,
            BillingCycle = billingCycle,
            CurrentPeriodStartUtc = nowUtc,
            CurrentPeriodEndUtc = periodEndUtc,
            NextRenewalAtUtc = periodEndUtc,
            AutoRenew = autoRenew,
            UnitPrice = unitPrice,
            PurchasedAtUtc = nowUtc,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        addOn.SetTenant(tenantId);
        return Result.Success(addOn);
    }

    public Result MarkPastDueBecauseRenewalFailed(string failureCode, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.Active)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot mark past due from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("AddOn.InvalidFailureCode", "FailureCode is required."));

        Status = AddOnStatus.PastDue;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromPastDue(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.PastDue)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot recover from {Status}."));

        Status = AddOnStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result EnterGracePeriodAfterRetriesExhausted(DateTime graceEndsAtUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.PastDue)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot enter grace period from {Status}."));

        if (graceEndsAtUtc <= nowUtc)
            return Result.Failure(new Error("AddOn.InvalidGracePeriod", "GraceEndsAtUtc must be in the future."));

        Status = AddOnStatus.GracePeriod;
        GracePeriodEndsAtUtc = graceEndsAtUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendBecauseGraceExpired(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.GracePeriod)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot suspend from {Status}."));

        Status = AddOnStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = "GraceExpired";
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReactivateAfterAdminReview(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.Suspended)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot reactivate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("AddOn.InvalidPeriod", "Period end must be after period start."));

        Status = AddOnStatus.Active;
        SuspendedAtUtc = null;
        SuspensionReason = null;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelActive(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, AddOnStatus.Active, AddOnStatus.PastDue, AddOnStatus.GracePeriod))
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("AddOn.InvalidReason", "Reason is required."));

        Status = AddOnStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterCancellationPeriodEnded(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.Cancelled)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot expire from {Status}."));

        Status = AddOnStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterSuspensionTimeout(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnStatus.Suspended)
            return Result.Failure(new Error("AddOn.InvalidTransition", $"Cannot expire from {Status}."));

        Status = AddOnStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static bool IsOneOf(AddOnStatus value, params AddOnStatus[] candidates)
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
