namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

/// <summary>Nunca incluye los secretos cifrados ni su plaintext — solo si están cargados
/// (<see cref="HasSecretKey"/>/<see cref="HasWebhookSecret"/>), para que el frontend sepa qué
/// falta sin exponer nada sensible. <see cref="HasPayments"/>: ya cobró con este proveedor; entonces no se
/// puede borrar (409 <c>TenantPaymentConfig.HasPayments</c>), solo desactivar. <see cref="WebhookSetup"/>:
/// <c>Automatic</c> | <c>Manual</c> | <c>Pending</c> | <c>Failed</c> | null (nunca intentado y sin manual).</summary>
public sealed record TenantPaymentConfigResponse(
    Guid Id,
    string ProviderCode,
    string Mode,
    string PublishableKey,
    bool HasSecretKey,
    bool HasWebhookSecret,
    string StatementDescriptor,
    bool IsActive,
    DateTime? SettledAtUtc,
    string? ApiBaseUrl,
    bool HasPayments,
    string? WebhookSetup
);
