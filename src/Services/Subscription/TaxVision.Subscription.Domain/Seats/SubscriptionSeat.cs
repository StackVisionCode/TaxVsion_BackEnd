using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>
/// Licencia individual con identidad propia, independiente de <see cref="Subscriptions.TenantSubscription"/>.
/// Un seat tiene su propio ciclo de facturación, su propio estado y su propia renovación.
/// </summary>
public sealed class SubscriptionSeat : TenantEntity
{
    private readonly List<SubscriptionSeatRenewal> _renewals = [];

    public SeatType Type { get; private set; }
    public SeatStatus Status { get; private set; }
    public SeatSourceType SourceType { get; private set; }
    public Guid? SourceReferenceId { get; private set; }
    public DateTime PurchasedAtUtc { get; private set; }
    public DateTime? CurrentPeriodStartUtc { get; private set; }
    public DateTime? CurrentPeriodEndUtc { get; private set; }
    public DateTime? NextRenewalAtUtc { get; private set; }
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

        // Copia propia del precio: Money es un owned type, y EF Core no puede compartir una instancia entre
        // filas — comprar varios asientos de una pasaba el MISMO objeto a todos, EF lo rastreaba una sola vez
        // y los demás se insertaban con UnitPriceAmount NULL. Se copia acá para que ningún llamador pueda
        // equivocarse: el precio no cambia, pero la instancia sí es de este asiento.
        var price = Money.Create(unitPrice.Amount, unitPrice.Currency);
        if (price.IsFailure)
            return Result.Failure<SubscriptionSeat>(price.Error);

        var seat = new SubscriptionSeat
        {
            Type = type,
            Status = SeatStatus.Available,
            SourceType = sourceType,
            SourceReferenceId = sourceReferenceId,
            PurchasedAtUtc = nowUtc,
            AutoRenew = autoRenew,
            BillingCycle = billingCycle,
            UnitPrice = price.Value,
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
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.PastDue))
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot activate from {Status}."));

        if (periodEndUtc <= periodStartUtc)
            return Result.Failure(new Error("Seat.InvalidPeriod", "Period end must be after period start."));

        Status = SeatStatus.Active;
        CurrentPeriodStartUtc = periodStartUtc;
        CurrentPeriodEndUtc = periodEndUtc;
        NextRenewalAtUtc = periodEndUtc;
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

    /// <summary>Al agotarse los reintentos de cobro (PastDue), se suspende de una.</summary>
    public Result SuspendAfterRetriesExhausted(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.PastDue)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot suspend from {Status}."));

        Status = SeatStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = "RenewalFailed";
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result SuspendForPolicyViolation(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsOneOf(Status, SeatStatus.Available, SeatStatus.Active, SeatStatus.PastDue))
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot suspend from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("Seat.InvalidReason", "Reason is required."));

        Status = SeatStatus.Suspended;
        SuspendedAtUtc = nowUtc;
        SuspensionReason = reason.Length > 500 ? reason[..500] : reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Recupera un seat Suspended O PastDue tras pago del admin: queda Active con un período nuevo
    /// (start = ahora, end = ahora + ciclo). PastDue se admite porque el admin paga antes de agotarse los
    /// reintentos — recuperamos el cupo sin esperar a que el dunning lo baje a Suspended.</summary>
    public Result ReactivateAfterAdminReview(
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != SeatStatus.Suspended && Status != SeatStatus.PastDue)
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
        if (!IsOneOf(Status, SeatStatus.Active, SeatStatus.PastDue))
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

    /// <summary>Programa el cargo del período parcial INICIAL (compra) sobre el período vigente sin
    /// avanzarlo — el equivalente de compra de <see cref="BeginRenewal"/>. Idempotente por
    /// <paramref name="idempotencyKey"/>. Reusa el pipeline de renovación: al confirmarse el pago,
    /// <see cref="CompleteRenewal"/> marca el intento exitoso sin mover el período (ya coincide con el
    /// vigente); si falla, <see cref="FailRenewal"/> lo lleva a PastDue → dunning como una renovación.</summary>
    public Result BeginInitialCharge(string idempotencyKey, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Active)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot begin initial charge from {Status}."));

        if (FindRenewalByKey(idempotencyKey) is not null)
            return Result.Success();

        var chargeResult = SubscriptionSeatRenewal.Schedule(
            Id,
            TenantId,
            idempotencyKey,
            CurrentPeriodStartUtc!.Value,
            CurrentPeriodEndUtc!.Value,
            nowUtc
        );
        if (chargeResult.IsFailure)
            return Result.Failure(chargeResult.Error);

        _renewals.Add(chargeResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Prepara el cobro para reactivar un seat Suspended/PastDue: crea un
    /// <see cref="SubscriptionSeatRenewal"/> con período NUEVO (ahora → ahora+ciclo) que al succeed se aplica
    /// vía <see cref="CompleteReactivation"/>. Es análogo a <see cref="BeginRenewal"/> pero exige
    /// Suspended/PastDue en vez de Active, y usa `nowUtc` como periodStart (no el fin del período anterior,
    /// que ya expiró). Idempotente por <paramref name="idempotencyKey"/>.</summary>
    public Result BeginReactivation(string idempotencyKey, DateTime newPeriodEndUtc, Guid actorUserId, DateTime nowUtc)
    {
        if (Status != SeatStatus.Suspended && Status != SeatStatus.PastDue)
            return Result.Failure(new Error("Seat.InvalidTransition", $"Cannot begin reactivation from {Status}."));

        if (newPeriodEndUtc <= nowUtc)
            return Result.Failure(new Error("Seat.InvalidPeriod", "Reactivation period end must be in the future."));

        if (FindRenewalByKey(idempotencyKey) is not null)
            return Result.Success();

        var renewalResult = SubscriptionSeatRenewal.Schedule(
            Id,
            TenantId,
            idempotencyKey,
            nowUtc,
            newPeriodEndUtc,
            nowUtc
        );
        if (renewalResult.IsFailure)
            return Result.Failure(renewalResult.Error);

        _renewals.Add(renewalResult.Value);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Aplica un pago exitoso de reactivación: marca el renewal succeeded y transiciona el seat
    /// Suspended/PastDue → Active con el período nuevo del renewal. Idempotente: si el renewal ya está
    /// Succeeded (reentrega del webhook, retry del consumer) el método es inert y no vuelve a mutar.</summary>
    public Result CompleteReactivation(
        Guid renewalId,
        string? externalPaymentReference,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var renewal = FindRenewalById(renewalId);
        if (renewal is null)
            return Result.Failure(new Error("Seat.RenewalNotFound", "Renewal does not exist."));

        // Reentrega: el renewal ya se cerró y el seat ya transicionó. Nada que hacer.
        if (renewal.Status == RenewalStatus.Succeeded)
            return Result.Success();

        var succeeded = CompleteRenewalAttempt(renewal, externalPaymentReference, nowUtc);
        if (succeeded.IsFailure)
            return succeeded;

        if (Status == SeatStatus.Active)
            return Result.Success();

        return ReactivateAfterAdminReview(renewal.PeriodStartUtc, renewal.PeriodEndUtc, actorUserId, nowUtc);
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

        // Mismo freno que la suscripción base: sin esto un seat/add-on sin tarjeta quedaba activo sin pagar.
        var effectiveWillRetry = RenewalRetryPolicy.ShouldRetry(willRetry, renewal.RetryCount, failureCode);
        var markResult = MarkRenewalAttemptFailed(
            renewal,
            failureCode,
            failureReason,
            effectiveWillRetry,
            effectiveWillRetry ? nextRetryAtUtc : null,
            nowUtc
        );
        if (markResult.IsFailure)
            return markResult;

        return effectiveWillRetry
            ? Result.Success()
            : MarkPastDueBecauseRenewalFailed(failureCode, actorUserId, nowUtc);
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
