using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Domain.SaaSPayments;

/// <summary>
/// Un intento de cobro de la plataforma a un tenant (renovación de suscripción, seats,
/// add-ons, upgrade de plan). Único por <see cref="IdempotencyKey"/> (unique index en
/// Infrastructure) — reintentar con la misma key nunca produce un segundo cobro.
///
/// Máquina de estados: <c>Pending → Processing → RequiresAction → Succeeded / Failed / Cancelled</c>.
/// Post-succeeded: <c>Refunded / PartiallyRefunded / ChargedBack</c>. Cada transición es un
/// método explícito con su propia precondición (guardrail §48.2) — no existe un
/// ChangeStatus(...) genérico. El aggregate nunca importa un SDK de provider (guardrail
/// §39.9): solo conoce <see cref="PaymentProviderCode"/> y VOs de intercambio.
/// </summary>
public sealed class SaaSPayment : TenantEntity
{
    private readonly List<SaaSPaymentAttempt> _attempts = [];
    private readonly List<RefundLine> _refunds = [];

    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public Money Amount { get; private set; } = null!;
    public SaaSPaymentType Type { get; private set; }
    public Guid TargetAggregateId { get; private set; }
    public PaymentProviderCode ProviderCode { get; private set; }
    public PaymentStatus Status { get; private set; }
    public ExternalPaymentReference? ExternalChargeReference { get; private set; }
    public StatementDescriptor StatementDescriptor { get; private set; } = null!;
    public string? NextActionType { get; private set; }
    public string? NextActionUrl { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime? NextRetryAtUtc { get; private set; }
    public DateTime? PaidAtUtc { get; private set; }
    public DateTime? ChargedBackAtUtc { get; private set; }
    public bool IsLegalHeld { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public IReadOnlyCollection<SaaSPaymentAttempt> Attempts => _attempts;
    public IReadOnlyCollection<RefundLine> Refunds => _refunds;

    private SaaSPayment() { }

    public static Result<SaaSPayment> Create(
        Guid tenantId,
        IdempotencyKey key,
        Money amount,
        SaaSPaymentType type,
        Guid targetAggregateId,
        PaymentProviderCode provider,
        StatementDescriptor descriptor,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<SaaSPayment>(new Error("SaaSPayment.InvalidTenant", "TenantId is required."));

        if (targetAggregateId == Guid.Empty)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.InvalidTarget", "TargetAggregateId is required.")
            );

        if (amount.AmountCents <= 0)
            return Result.Failure<SaaSPayment>(
                new Error("SaaSPayment.InvalidAmount", "Amount must be greater than zero.")
            );

        var payment = new SaaSPayment
        {
            IdempotencyKey = key,
            Amount = amount,
            Type = type,
            TargetAggregateId = targetAggregateId,
            ProviderCode = provider,
            Status = PaymentStatus.Pending,
            StatementDescriptor = descriptor,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        payment.SetTenant(tenantId);
        return Result.Success(payment);
    }

    /// <summary>Registra el envío del cobro al provider y agrega el intento correspondiente
    /// al historial auditable.</summary>
    public Result MarkProcessing(
        ExternalPaymentReference reference,
        string? providerResponseCode,
        string? providerResponseBody,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark processing from {Status}."));

        ExternalChargeReference = reference;
        Status = PaymentStatus.Processing;
        _attempts.Add(
            SaaSPaymentAttempt.Record(
                Id,
                TenantId,
                _attempts.Count + 1,
                providerResponseCode,
                providerResponseBody,
                nowUtc
            )
        );
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>El provider requiere una acción adicional del pagador (3DS / SCA) antes de
    /// poder confirmar el cobro.</summary>
    public Result MarkRequiresAction(string nextActionType, string nextActionUrl, Guid actorUserId, DateTime nowUtc)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot require action from {Status}."));

        if (string.IsNullOrWhiteSpace(nextActionType))
            return Result.Failure(new Error("SaaSPayment.InvalidNextAction", "NextActionType is required."));

        Status = PaymentStatus.RequiresAction;
        NextActionType = nextActionType;
        NextActionUrl = nextActionUrl;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkSucceeded(DateTime paidAtUtc, Guid actorUserId)
    {
        if (Status is not (PaymentStatus.Processing or PaymentStatus.RequiresAction))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark succeeded from {Status}."));

        Status = PaymentStatus.Succeeded;
        PaidAtUtc = paidAtUtc;
        NextActionType = null;
        NextActionUrl = null;
        Touch(actorUserId, paidAtUtc);
        return Result.Success();
    }

    public Result MarkFailed(
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (
            Status
            is PaymentStatus.Succeeded
                or PaymentStatus.Refunded
                or PaymentStatus.PartiallyRefunded
                or PaymentStatus.Cancelled
                or PaymentStatus.ChargedBack
        )
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot mark failed from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("SaaSPayment.InvalidFailureCode", "FailureCode is required."));

        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureReason = failureReason;
        NextRetryAtUtc = willRetry ? nextRetryAtUtc : null;
        NextActionType = null;
        NextActionUrl = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>Reabre un pago Failed para un nuevo intento — lo usa
    /// <c>RetrySaaSPaymentHandler</c> (dunning), nunca un caller directo. Solo procede si el
    /// intento anterior dejó agendado un retry (<see cref="NextRetryAtUtc"/> no nulo) y ya
    /// llegó su hora.</summary>
    public Result PrepareForRetry(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Failed)
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot retry from {Status}."));

        if (NextRetryAtUtc is null)
            return Result.Failure(new Error("SaaSPayment.NoRetryScheduled", "This payment has no retry scheduled."));

        if (nowUtc < NextRetryAtUtc)
            return Result.Failure(
                new Error("SaaSPayment.RetryNotDue", "The scheduled retry time has not arrived yet.")
            );

        Status = PaymentStatus.Pending;
        FailureCode = null;
        FailureReason = null;
        NextRetryAtUtc = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result CancelByAdmin(string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (IsLegalHeld)
            return Result.Failure(
                new Error("Payment.LegalHeld", "Payment is under legal hold and cannot be cancelled.")
            );

        if (
            Status
            is not (
                PaymentStatus.Pending
                or PaymentStatus.Processing
                or PaymentStatus.RequiresAction
                or PaymentStatus.Failed
            )
        )
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        Status = PaymentStatus.Cancelled;
        FailureReason = reason;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RefundPartial(Money refundAmount, string reason, Guid actorUserId, DateTime nowUtc)
    {
        if (IsLegalHeld)
            return Result.Failure(
                new Error("Payment.LegalHeld", "Payment is under legal hold and cannot be refunded.")
            );

        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Result.Failure(new Error("SaaSPayment.InvalidTransition", $"Cannot refund from {Status}."));

        if (refundAmount.Currency != Amount.Currency)
            return Result.Failure(
                new Error("SaaSPayment.CurrencyMismatch", "Refund currency must match the original payment currency.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        var totalRefunded = SumOfRefunds();
        if (totalRefunded + refundAmount.AmountCents > Amount.AmountCents)
            return Result.Failure(
                new Error("SaaSPayment.RefundExceedsPrincipal", "Refund amount exceeds the original payment amount.")
            );

        _refunds.Add(RefundLine.Create(Id, TenantId, refundAmount, reason, actorUserId, nowUtc));

        Status =
            totalRefunded + refundAmount.AmountCents == Amount.AmountCents
                ? PaymentStatus.Refunded
                : PaymentStatus.PartiallyRefunded;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result RefundFull(string reason, Guid actorUserId, DateTime nowUtc)
    {
        var remaining = Amount.Subtract(Money.Create(SumOfRefunds(), Amount.Currency).Value);
        if (remaining.IsFailure)
            return Result.Failure(remaining.Error);

        return RefundPartial(remaining.Value, reason, actorUserId, nowUtc);
    }

    public Result MarkChargedBack(DateTime chargedBackAtUtc, string reason, Guid actorUserId)
    {
        if (Status is not (PaymentStatus.Succeeded or PaymentStatus.PartiallyRefunded))
            return Result.Failure(
                new Error("SaaSPayment.InvalidTransition", $"Cannot mark charged back from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("SaaSPayment.InvalidReason", "Reason is required."));

        Status = PaymentStatus.ChargedBack;
        ChargedBackAtUtc = chargedBackAtUtc;
        FailureReason = reason;
        Touch(actorUserId, chargedBackAtUtc);
        return Result.Success();
    }

    /// <summary>Coloca o levanta el legal hold. Reservado para <c>PlatformAdmin</c> con el
    /// permiso <c>payment_app.legal_hold.manage</c> (verificado en el handler, no aquí).</summary>
    public Result SetLegalHold(bool isLegalHeld, Guid actorUserId, DateTime nowUtc)
    {
        IsLegalHeld = isLegalHeld;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private long SumOfRefunds()
    {
        long total = 0;
        foreach (var line in _refunds)
            total += line.Amount.AmountCents;
        return total;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
