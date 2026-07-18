using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>
/// Licencia individual con identidad propia, independiente de <see cref="Subscriptions.TenantSubscription"/>.
/// Un seat tiene su propio ciclo de facturación, su propio estado, su propia renovación
/// y su propia asignación a un empleado del tenant.
/// </summary>
public sealed class SubscriptionSeat : TenantEntity
{
    private readonly List<SubscriptionSeatAssignment> _assignments = [];
    private readonly List<SubscriptionSeatRenewal> _renewals = [];

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
    public Guid? CurrentAssignmentId { get; private set; }
    public Guid? CurrentUserId { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<SubscriptionSeatAssignment> Assignments => _assignments;
    public IReadOnlyCollection<SubscriptionSeatRenewal> Renewals => _renewals;

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
        DateTime nowUtc
    )
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

    /// <summary>Asigna el seat a un empleado del tenant. Requiere que el seat esté
    /// disponible y que no haya sido liberado hace menos de <paramref name="reassignmentCooldownDays"/> días.</summary>
    public Result AssignTo(Guid userId, Guid actorUserId, DateTime nowUtc, int reassignmentCooldownDays)
    {
        // Status y CurrentUserId siempre cambian juntos (ver ReleaseCurrentAssignment), así
        // que Status == Available ya implica que no hay assignment vigente.
        if (Status != SeatStatus.Available)
            return Result.Failure(new Error("Seat.NotAvailable", $"Seat is {Status}."));

        if (reassignmentCooldownDays > 0)
        {
            var mostRecentRelease = FindMostRecentReleaseUtc();
            if (
                mostRecentRelease is not null
                && (nowUtc - mostRecentRelease.Value).TotalDays < reassignmentCooldownDays
            )
            {
                return Result.Failure(
                    new Error(
                        "Seat.ReassignmentCooldown",
                        $"Wait {reassignmentCooldownDays} day(s) after release before reassigning."
                    )
                );
            }
        }

        var assignmentResult = SubscriptionSeatAssignment.Create(Id, TenantId, userId, actorUserId, nowUtc);
        if (assignmentResult.IsFailure)
            return assignmentResult;

        _assignments.Add(assignmentResult.Value);
        CurrentAssignmentId = assignmentResult.Value.Id;
        CurrentUserId = userId;
        Status = SeatStatus.Assigned;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Libera la asignación vigente. El seat vuelve a Available si aún no estaba
    /// activo (pago pendiente), o se mantiene en su estado de billing si ya estaba pagado.</summary>
    public Result ReleaseCurrentAssignment(Guid actorUserId, DateTime nowUtc, string? reason)
    {
        if (CurrentUserId is null || CurrentAssignmentId is null)
            return Result.Failure(new Error("Seat.NotAssigned", "Seat has no active assignment."));

        var active = FindAssignmentById(CurrentAssignmentId.Value);
        if (active is null)
            return Result.Failure(new Error("Seat.NotAssigned", "Seat has no active assignment."));

        var releaseResult = active.Release(actorUserId, nowUtc, reason);
        if (releaseResult.IsFailure)
            return releaseResult;

        CurrentAssignmentId = null;
        CurrentUserId = null;
        if (Status == SeatStatus.Assigned)
            Status = SeatStatus.Available;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Reasigna el seat a otro usuario: libera la asignación vigente y crea una
    /// nueva en la misma operación, aplicando el cooldown de reasignación.</summary>
    public Result ReassignSeat(
        Guid toUserId,
        Guid actorUserId,
        DateTime nowUtc,
        string? releaseReason,
        int reassignmentCooldownDays
    )
    {
        var releaseResult = ReleaseCurrentAssignment(actorUserId, nowUtc, releaseReason);
        if (releaseResult.IsFailure)
            return releaseResult;

        return AssignTo(toUserId, actorUserId, nowUtc, reassignmentCooldownDays);
    }

    public Result Activate(DateTime periodStartUtc, DateTime periodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.Assigned, SeatStatus.PastDue, SeatStatus.GracePeriod))
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
        if (
            !IsOneOf(
                Status,
                SeatStatus.Available,
                SeatStatus.Assigned,
                SeatStatus.Active,
                SeatStatus.PastDue,
                SeatStatus.GracePeriod
            )
        )
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot suspend from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Seat.InvalidReason", "Reason is required."));

        Status = SeatStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result ReactivateAfterAdminReview(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
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
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.Assigned))
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

    /// <summary>Programa una renovación de este seat, independiente de la suscripción
    /// base y de cualquier otro seat. Idempotente por <paramref name="idempotencyKey"/>.</summary>
    public Result BeginRenewal(string idempotencyKey, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Active)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot begin renewal from {Status}."));

        if (FindRenewalByKey(idempotencyKey) is not null)
            return Result.Success();

        var newPeriodEndUtc = BillingCycle.CalculateNext(CurrentPeriodEndUtc!.Value);
        var renewalResult = SubscriptionSeatRenewal.Schedule(
            Id,
            TenantId,
            idempotencyKey,
            CurrentPeriodEndUtc.Value,
            newPeriodEndUtc,
            nowUtc
        );
        if (renewalResult.IsFailure)
            return Result.Failure(renewalResult.Error);

        _renewals.Add(renewalResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Aplica una renovación exitosa: avanza el período del seat. No toca la
    /// suscripción base ni otros seats.</summary>
    public Result CompleteRenewal(Guid renewalId, string? externalPaymentReference, Guid actorUserId, DateTime nowUtc)
    {
        var renewal = FindRenewalById(renewalId);
        if (renewal is null)
            return Result.Failure(new Error("Seat.RenewalNotFound", "Renewal does not exist."));

        var succeeded = CompleteRenewalAttempt(renewal, externalPaymentReference, nowUtc);
        if (succeeded.IsFailure)
            return succeeded;

        if (Status != SeatStatus.Active)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot apply renewal while {Status}."));

        CurrentPeriodStartUtc = renewal.PeriodStartUtc;
        CurrentPeriodEndUtc = renewal.PeriodEndUtc;
        NextRenewalAtUtc = renewal.PeriodEndUtc;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Registra el fallo de una renovación de seat. Si <paramref name="willRetry"/>
    /// es false agota los reintentos y transiciona el seat a PastDue (solo afecta a este
    /// seat, no a la suscripción base ni a otros seats).</summary>
    public Result FailRenewal(
        Guid renewalId,
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var renewal = FindRenewalById(renewalId);
        if (renewal is null)
            return Result.Failure(new Error("Seat.RenewalNotFound", "Renewal does not exist."));

        var markResult = MarkRenewalAttemptFailed(
            renewal,
            failureCode,
            failureReason,
            willRetry,
            nextRetryAtUtc,
            nowUtc
        );
        if (markResult.IsFailure)
            return markResult;

        return willRetry ? Result.Success() : MarkPastDueBecauseRenewalFailed(failureCode, actorUserId, nowUtc);
    }

    private static Result CompleteRenewalAttempt(
        SubscriptionSeatRenewal renewal,
        string? externalPaymentReference,
        DateTime nowUtc
    )
    {
        if (renewal.Status != RenewalStatus.Processing)
        {
            var processing = renewal.MarkProcessing(nowUtc);
            if (processing.IsFailure)
                return processing;
        }

        return renewal.MarkSucceeded(externalPaymentReference, nowUtc);
    }

    private static Result MarkRenewalAttemptFailed(
        SubscriptionSeatRenewal renewal,
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        DateTime nowUtc
    )
    {
        if (renewal.Status != RenewalStatus.Processing)
        {
            var processing = renewal.MarkProcessing(nowUtc);
            if (processing.IsFailure)
                return processing;
        }

        return renewal.MarkFailed(failureCode, failureReason, willRetry, nextRetryAtUtc, nowUtc);
    }

    private SubscriptionSeatRenewal? FindRenewalByKey(string idempotencyKey)
    {
        foreach (var renewal in _renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }

    private SubscriptionSeatRenewal? FindRenewalById(Guid renewalId)
    {
        foreach (var renewal in _renewals)
        {
            if (renewal.Id == renewalId)
                return renewal;
        }

        return null;
    }

    private DateTime? FindMostRecentReleaseUtc()
    {
        DateTime? mostRecent = null;
        foreach (var assignment in _assignments)
        {
            if (assignment.ReleasedAtUtc is null)
                continue;

            if (mostRecent is null || assignment.ReleasedAtUtc > mostRecent)
                mostRecent = assignment.ReleasedAtUtc;
        }

        return mostRecent;
    }

    private SubscriptionSeatAssignment? FindAssignmentById(Guid assignmentId)
    {
        foreach (var assignment in _assignments)
        {
            if (assignment.Id == assignmentId)
                return assignment;
        }

        return null;
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
