using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Domain.PaymentLinks;

/// <summary>
/// URL mágico de pago: el tenant lo genera con un monto y una expiración, se lo manda al
/// taxpayer (email/QR), y el taxpayer paga sin necesitar cuenta ni JWT — el
/// <see cref="Token"/> es la única prueba de posesión (§32.2 "checkout" del diseño).
///
/// <see cref="RelatedTenantPaymentId"/> se completa en <see cref="AttachPaymentAttempt"/> tan
/// pronto se crea un <c>TenantPayment</c> para un intento de canje — antes de saber si el
/// cobro va a terminar en éxito. Esto permite que un cobro que queda en <c>RequiresAction</c>
/// (3DS) se resuelva más tarde vía webhook y aun así encuentre el link que lo originó.
/// </summary>
public sealed class PaymentLink : TenantEntity
{
    public Guid? TaxpayerId { get; private set; }
    public Money Amount { get; private set; } = null!;
    public PaymentPurpose Purpose { get; private set; } = null!;
    public PaymentLinkToken Token { get; private set; } = null!;
    public PaymentLinkStatus Status { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UsedAtUtc { get; private set; }
    public Guid? RelatedTenantPaymentId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public int FailedRedemptionAttempts { get; private set; }

    private PaymentLink() { }

    public static Result<PaymentLink> Create(
        Guid tenantId,
        Guid? taxpayerId,
        Money amount,
        PaymentPurpose purpose,
        PaymentLinkToken token,
        TimeSpan expiration,
        Guid actorUserId,
        DateTime nowUtc)
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<PaymentLink>(new Error("PaymentLink.InvalidTenant", "TenantId is required."));

        if (amount.AmountCents <= 0)
            return Result.Failure<PaymentLink>(new Error("PaymentLink.InvalidAmount", "Amount must be greater than zero."));

        if (expiration <= TimeSpan.Zero || expiration > TimeSpan.FromDays(30))
            return Result.Failure<PaymentLink>(new Error("PaymentLink.InvalidExpiration", "Expiration must be between 1 second and 30 days."));

        var link = new PaymentLink
        {
            TaxpayerId = taxpayerId,
            Amount = amount,
            Purpose = purpose,
            Token = token,
            Status = PaymentLinkStatus.Active,
            ExpiresAtUtc = nowUtc.Add(expiration),
            CreatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
        };
        link.SetTenant(tenantId);
        return Result.Success(link);
    }

    /// <summary>Un link vencido (aunque su fila siga en <c>Active</c> porque el job de
    /// expiración todavía no corrió) nunca es redimible — el chequeo de tiempo es la fuente
    /// de verdad, no solo el status persistido.</summary>
    public bool IsRedeemable(DateTime nowUtc) => Status == PaymentLinkStatus.Active && nowUtc < ExpiresAtUtc;

    public Result AttachPaymentAttempt(Guid tenantPaymentId, DateTime nowUtc)
    {
        if (!IsRedeemable(nowUtc))
            return Result.Failure(new Error("PaymentLink.NotRedeemable", $"Cannot attach a payment attempt while link is {Status}."));

        RelatedTenantPaymentId = tenantPaymentId;
        return Result.Success();
    }

    public Result MarkAsUsed(DateTime nowUtc)
    {
        if (Status != PaymentLinkStatus.Active)
            return Result.Failure(new Error("PaymentLink.InvalidTransition", $"Cannot mark used from {Status}."));

        if (RelatedTenantPaymentId is null)
            return Result.Failure(new Error("PaymentLink.NoPaymentAttempt", "No payment attempt is attached to this link."));

        Status = PaymentLinkStatus.Used;
        UsedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result Revoke(string reason, DateTime nowUtc)
    {
        if (Status != PaymentLinkStatus.Active)
            return Result.Failure(new Error("PaymentLink.InvalidTransition", $"Cannot revoke from {Status}."));

        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(new Error("PaymentLink.InvalidReason", "Reason is required."));

        Status = PaymentLinkStatus.Revoked;
        return Result.Success();
    }

    public Result Expire(DateTime nowUtc)
    {
        if (Status != PaymentLinkStatus.Active)
            return Result.Failure(new Error("PaymentLink.InvalidTransition", $"Cannot expire from {Status}."));

        Status = PaymentLinkStatus.Expired;
        return Result.Success();
    }

    /// <summary>Cuenta un intento de canje fallido (tarjeta declinada, error del provider) y
    /// revoca el link automáticamente al llegar a
    /// <see cref="PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink"/> — sin este freno el
    /// token del link (única prueba de posesión) permitiría probar tarjetas sin límite contra
    /// el monto fijado. No-op si el link ya no está Active.</summary>
    public void MarkBlockedAfterExcessiveFailures(DateTime nowUtc)
    {
        if (Status != PaymentLinkStatus.Active)
            return;

        FailedRedemptionAttempts++;
        if (FailedRedemptionAttempts >= PaymentLinkAttemptPolicy.MaxRedemptionAttemptsPerLink)
            Status = PaymentLinkStatus.Revoked;
    }
}
