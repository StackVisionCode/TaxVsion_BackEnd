using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.TenantPayments;

/// <summary>
/// Un intento de cobro del tenant a un taxpayer (contribuyente/cliente del tenant). Único
/// por <see cref="IdempotencyKey"/>. <see cref="TaxpayerId"/> es opcional — permite guest
/// checkout vía <c>PaymentLink</c> (Fase F) sin que el taxpayer tenga cuenta.
///
/// Máquina de estados: <c>Pending → Processing → RequiresAction → Succeeded / Failed / Cancelled</c>.
/// Post-succeeded: <c>Refunded / PartiallyRefunded / ChargedBack</c>. El aggregate nunca
/// importa un SDK de provider — solo conoce <see cref="PaymentProviderCode"/> y VOs de
/// intercambio.
/// </summary>
public sealed class TenantPayment : TenantEntity
{
    private readonly List<TenantPaymentAttempt> _attempts = [];
    private readonly List<RefundLine> _refunds = [];

    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public Money Amount { get; private set; } = null!;
    public Guid? TaxpayerId { get; private set; }
    public PaymentPurpose Purpose { get; private set; } = null!;
    public PaymentProviderCode ProviderCode { get; private set; }
    public PaymentStatus Status { get; private set; }
    public ExternalPaymentReference? ExternalChargeReference { get; private set; }

    /// <summary>Solo se completa cuando el cobro fue vía <see cref="TenantPaymentConfigs.TenantPaymentMode.Connect"/>
    /// — el charge vive en la cuenta del tenant, no en la de la plataforma (§18.4/§19.4 del
    /// diseño), así que reconciliar/reembolsar requiere el <c>Stripe-Account</c> header
    /// reconstruido desde <c>TenantConnectAccount.StripeConnectAccountId</c>.</summary>
    public string? ProviderChargeReferenceOnConnect { get; private set; }
    public SplitPaymentBreakdown? SplitPayment { get; private set; }
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

    public IReadOnlyCollection<TenantPaymentAttempt> Attempts => _attempts;
    public IReadOnlyCollection<RefundLine> Refunds => _refunds;

    private TenantPayment() { }

    public static Result<TenantPayment> Create(
        Guid tenantId,
        IdempotencyKey key,
        Money amount,
        Guid? taxpayerId,
        PaymentPurpose purpose,
        PaymentProviderCode provider,
        StatementDescriptor descriptor,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<TenantPayment>(new Error("TenantPayment.InvalidTenant", "TenantId is required."));

        if (amount.AmountCents <= 0)
            return Result.Failure<TenantPayment>(
                new Error("TenantPayment.InvalidAmount", "Amount must be greater than zero.")
            );

        var payment = new TenantPayment
        {
            IdempotencyKey = key,
            Amount = amount,
            TaxpayerId = taxpayerId,
            Purpose = purpose,
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

    public Result MarkProcessing(
        ExternalPaymentReference reference,
        string? providerResponseCode,
        string? providerResponseBody,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(
                new Error("TenantPayment.InvalidTransition", $"Cannot mark processing from {Status}.")
            );

        ExternalChargeReference = reference;
        Status = PaymentStatus.Processing;
        _attempts.Add(
            TenantPaymentAttempt.Record(
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

    /// <summary>Equivalente de <see cref="MarkProcessing"/> para un cobro hecho vía
    /// <see cref="TenantPaymentConfigs.TenantPaymentMode.Connect"/> — puebla
    /// <see cref="ProviderChargeReferenceOnConnect"/> y <see cref="SplitPayment"/> en vez de
    /// <see cref="ExternalChargeReference"/>. Hace cumplir el invariante §21.2.6: el split
    /// (tenant + platform fee) debe sumar exactamente <see cref="Amount"/>.</summary>
    public Result MarkProcessingViaConnect(
        string providerChargeReferenceOnConnect,
        SplitPaymentBreakdown split,
        string? providerResponseCode,
        string? providerResponseBody,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (Status != PaymentStatus.Pending)
            return Result.Failure(
                new Error("TenantPayment.InvalidTransition", $"Cannot mark processing from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(providerChargeReferenceOnConnect))
            return Result.Failure(
                new Error("TenantPayment.InvalidReference", "ProviderChargeReferenceOnConnect is required.")
            );

        if (split.TenantAmountCents + split.PlatformFeeAmountCents != Amount.AmountCents)
            return Result.Failure(
                new Error("TenantPayment.SplitMismatch", "Split total must equal the payment amount.")
            );

        ProviderChargeReferenceOnConnect = providerChargeReferenceOnConnect;
        SplitPayment = split;
        Status = PaymentStatus.Processing;
        _attempts.Add(
            TenantPaymentAttempt.Record(
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

    public Result MarkRequiresAction(string nextActionType, string nextActionUrl, Guid actorUserId, DateTime nowUtc)
    {
        if (Status is not (PaymentStatus.Pending or PaymentStatus.Processing))
            return Result.Failure(
                new Error("TenantPayment.InvalidTransition", $"Cannot require action from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(nextActionType))
            return Result.Failure(new Error("TenantPayment.InvalidNextAction", "NextActionType is required."));

        Status = PaymentStatus.RequiresAction;
        NextActionType = nextActionType;
        NextActionUrl = nextActionUrl;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result MarkSucceeded(DateTime paidAtUtc, Guid actorUserId)
    {
        if (Status is not (PaymentStatus.Processing or PaymentStatus.RequiresAction))
            return Result.Failure(
                new Error("TenantPayment.InvalidTransition", $"Cannot mark succeeded from {Status}.")
            );

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
            return Result.Failure(new Error("TenantPayment.InvalidTransition", $"Cannot mark failed from {Status}."));

        if (string.IsNullOrWhiteSpace(failureCode))
            return Result.Failure(new Error("TenantPayment.InvalidFailureCode", "FailureCode is required."));

        Status = PaymentStatus.Failed;
        FailureCode = failureCode;
        FailureReason = failureReason;
        NextRetryAtUtc = willRetry ? nextRetryAtUtc : null;
        NextActionType = null;
        NextActionUrl = null;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result PrepareForRetry(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != PaymentStatus.Failed)
            return Result.Failure(new Error("TenantPayment.InvalidTransition", $"Cannot retry from {Status}."));

        if (NextRetryAtUtc is null)
            return Result.Failure(new Error("TenantPayment.NoRetryScheduled", "This payment has no retry scheduled."));

        if (nowUtc < NextRetryAtUtc)
            return Result.Failure(
                new Error("TenantPayment.RetryNotDue", "The scheduled retry time has not arrived yet.")
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
            return Result.Failure(new Error("TenantPayment.InvalidTransition", $"Cannot cancel from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantPayment.InvalidReason", "Reason is required."));

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
            return Result.Failure(new Error("TenantPayment.InvalidTransition", $"Cannot refund from {Status}."));

        if (refundAmount.Currency != Amount.Currency)
            return Result.Failure(
                new Error("TenantPayment.CurrencyMismatch", "Refund currency must match the original payment currency.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantPayment.InvalidReason", "Reason is required."));

        var totalRefunded = SumOfRefunds();
        if (totalRefunded + refundAmount.AmountCents > Amount.AmountCents)
            return Result.Failure(
                new Error("TenantPayment.RefundExceedsPrincipal", "Refund amount exceeds the original payment amount.")
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
                new Error("TenantPayment.InvalidTransition", $"Cannot mark charged back from {Status}.")
            );

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("TenantPayment.InvalidReason", "Reason is required."));

        Status = PaymentStatus.ChargedBack;
        ChargedBackAtUtc = chargedBackAtUtc;
        FailureReason = reason;
        Touch(actorUserId, chargedBackAtUtc);
        return Result.Success();
    }

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
