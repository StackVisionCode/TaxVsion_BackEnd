using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.DeleteTenantPaymentConfig;

/// <summary>Elimina por completo la config de un proveedor para el tenant — para corregir un alta
/// errónea (proveedor/clave equivocada). Idempotente: si no existe, es no-op exitoso. Con pagos de ese
/// proveedor → 409 <c>TenantPaymentConfig.HasPayments</c>: hay que desactivarlo (sus webhooks siguen
/// verificándose con el secreto guardado).</summary>
public sealed record DeleteTenantPaymentConfigCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    Guid ActorUserId
);
