using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Commands.RegisterTenantPaymentWebhook;

/// <summary>Comando interno (cola local durable): crea el webhook de la oficina en el proveedor.
/// <paramref name="Attempt"/> empieza en 1; los reintentos se reprograman con el siguiente.</summary>
public sealed record RegisterTenantPaymentWebhookCommand(
    Guid TenantId,
    PaymentProviderCode ProviderCode,
    int Attempt = 1
);
