using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;

/// <summary>
/// <see cref="PaymentMethodReference"/> ya viene tokenizado por el frontend (p.ej. Stripe
/// Elements) — el backend nunca recibe ni toca un número de tarjeta crudo. El backend cobra
/// directo contra ese método; no existe un customer guardado para PaymentClient en esta fase
/// (guest checkout).
///
/// <see cref="PlatformFeeAmountCents"/> solo aplica cuando el <c>TenantPaymentConfig</c> del
/// tenant está en modo <see cref="TaxVision.PaymentClient.Domain.TenantPaymentConfigs.TenantPaymentMode.Connect"/>
/// — el split (tenant + fee) lo decide quien dispara el cobro, no el handler (§33.3 del
/// diseño: <c>SplitPaymentBreakdown? Split</c> viaja en el command). En modo DirectApiKeys se
/// ignora.
/// </summary>
public sealed record ChargeTenantPaymentCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    long AmountCents,
    string Currency,
    Guid? TaxpayerId,
    PaymentPurposeKind PurposeKind,
    string? PurposeExternalReferenceId,
    string PaymentMethodReference,
    string? ReceiptEmail,
    string IdempotencyKey,
    Guid ActorUserId,
    long? PlatformFeeAmountCents = null,
    string? PlatformFeeReference = null
);
