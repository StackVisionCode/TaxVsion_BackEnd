using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>
/// Licencia individual con identidad propia, independiente de <see cref="Subscriptions.TenantSubscription"/>.
/// Un seat tiene su propio ciclo de facturación, su propio estado y su propia renovación
/// (la renovación llega en una fase posterior). La asignación a un empleado del tenant
/// (<c>SubscriptionSeatAssignment</c>) llega también en una fase posterior — este aggregate
/// cubre únicamente el ciclo comercial del seat en sí.
/// </summary>
public sealed class SubscriptionSeat : TenantEntity
{
    public SeatType Type { get; private set; }
    public SeatStatus Status { get; private set; }
    public SeatSourceType SourceType { get; private set; }
    public Guid? SourceReferenceId { get; private set; }
    public DateTime PurchasedAtUtc { get; private set; }
    public DateTime? CurrentPeriodStartUtc { get; private set; }
    public DateTime? CurrentPeriodEndUtc { get; private set; }
    public DateTime? NextRenewalAtUtc { get; private set; }
    public DateTime? GracePeriodEndsAtUtc { get; private set; }
    public bool AutoRenew { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public Money UnitPrice { get; private set; } = null!;
    public DateTime? CancelledAtUtc { get; private set; }
    public DateTime? SuspendedAtUtc { get; private set; }
    public DateTime? ExpiredAtUtc { get; private set; }
    public string? CancellationReason { get; private set; }
    public string? SuspensionReason { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    private SubscriptionSeat() { }

    public static Result<SubscriptionSeat> Purchase(
        Guid tenantId,
        SeatType type,
        SeatSourceType sourceType,
        Guid? sourceReferenceId,
        Money unitPrice,
        BillingCycle billingCycle,
        bool autoRenew,
        Guid actorUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SubscriptionSeat>(new Error("Seat.InvalidTenant", "TenantId is required."));

        var seat = new SubscriptionSeat
        {
            Type = type,
            Status = SeatStatus.Available,
            SourceType = sourceType,
            SourceReferenceId = sourceReferenceId,
            PurchasedAtUtc = nowUtc,
            AutoRenew = autoRenew,
            BillingCycle = billingCycle,
            UnitPrice = unitPrice,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        seat.SetTenant(tenantId);
        return Result.Success(seat);
    }

    public Result Activate(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.PastDue, SeatStatus.GracePeriod))
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot activate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Seat.InvalidPeriod", "Period end must be after period start."));

        Status = SeatStatus.Active;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
        GracePeriodEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkPastDueBecauseRenewalFailed(string failureCode, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Active)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot mark past due from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("Seat.InvalidFailureCode", "FailureCode is required."));

        Status = SeatStatus.PastDue;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromPastDue(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.PastDue)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SeatStatus.Active;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result EnterGracePeriodAfterRetriesExhausted(DateTime graceEndsAtUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.PastDue)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot enter grace period from {Status}."));

        if (graceEndsAtUtc <= nowUtc)
            return Result.Failure(new Error("Seat.InvalidGracePeriod", "GraceEndsAtUtc must be in the future."));

        Status = SeatStatus.GracePeriod;
        GracePeriodEndsAtUtc = graceEndsAtUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RecoverFromGracePeriod(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.GracePeriod)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot recover from {Status}."));

        Status = SeatStatus.Active;
        GracePeriodEndsAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendBecauseGraceExpired(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.GracePeriod)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot suspend from {Status}."));

        Status = SeatStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = "GraceExpired";
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendForPolicyViolation(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.Active, SeatStatus.PastDue, SeatStatus.GracePeriod))
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot suspend from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Seat.InvalidReason", "Reason is required."));

        Status = SeatStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReactivateAfterAdminReview(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Suspended)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot reactivate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Seat.InvalidPeriod", "Period end must be after period start."));

        Status = SeatStatus.Active;
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
        if (Status != SeatStatus.Suspended)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SeatStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelBeforeActivation(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Available)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Seat.InvalidReason", "Reason is required."));

        Status = SeatStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelActive(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SeatStatus.Active, SeatStatus.PastDue, SeatStatus.GracePeriod))
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Seat.InvalidReason", "Reason is required."));

        Status = SeatStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancellationReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ExpireAfterCancellationPeriodEnded(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Cancelled)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot expire from {Status}."));

        Status = SeatStatus.Expired;
        ExpiredAtUtc = nowUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private static bool IsOneOf(SeatStatus value, params SeatStatus[] candidates)
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
