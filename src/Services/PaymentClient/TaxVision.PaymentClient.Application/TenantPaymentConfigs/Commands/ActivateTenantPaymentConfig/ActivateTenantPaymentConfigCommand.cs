using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.ActivateTenantPaymentConfig;

/// <summary>Activa (habilita) el método de pago del tenant para ese proveedor. En modo DirectApiKeys
/// exige que los secretos ya estén cargados; en modo Connect usa la ruta de Connect.</summary>
public sealed record ActivateTenantPaymentConfigCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    Guid ActorUserId
);
