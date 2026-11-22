using TaxVision.Referrals.Domain.Programs;

namespace TaxVision.Referrals.Application.Qualifications.QualifyReferral;

/// <param name="CorrelationId">La del pago que califica; viaja en el aviso al referidor.</param>
public sealed record QualifyReferralCommand(
    Guid TenantId,
    Guid AttributionId,
    Guid QualifyingEventId,
    Guid PaymentId,
    QualifyingPaymentSource PaymentSource,
    long PaymentAmountCents,
    string PaymentCurrency,
    bool IsFirstSuccessfulPayment,
    DateTime PaymentSucceededAtUtc,
    string IdempotencyKey,
    Guid ActorUserId,
    string CorrelationId
);
