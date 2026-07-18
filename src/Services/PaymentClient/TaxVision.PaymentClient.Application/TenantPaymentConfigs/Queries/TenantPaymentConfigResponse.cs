namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

/// <summary>Nunca incluye los secretos cifrados ni su plaintext — solo si están cargados
/// (<see cref="HasSecretKey"/>/<see cref="HasWebhookSecret"/>), para que el frontend sepa qué
/// falta sin exponer nada sensible.</summary>
public sealed record TenantPaymentConfigResponse(
    Guid Id,
    string ProviderCode,
    string Mode,
    string PublishableKey,
    bool HasSecretKey,
    bool HasWebhookSecret,
    string StatementDescriptor,
    bool IsActive,
    DateTime? SettledAtUtc
);
