using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Application.Abstractions.Payments;

/// <summary>
/// Lo que un adaptador necesita que la oficina cargue: etiquetas de las claves, si usa webhook secret
/// y si acepta URL de API. Es la fuente del catálogo que ve el CRM (solo adaptadores registrados).
/// <see cref="CredentialsHelpUrl"/>: página oficial del proveedor donde se sacan las claves.
/// <see cref="WebhookEvents"/>/<see cref="WebhookRouteName"/>: eventos que procesa el adaptador y nombre de la
/// ruta que los recibe (la API arma con él la URL de webhook de cada oficina).
/// <see cref="WebhookSecretLabel"/>: cómo llama el proveedor al dato que guardamos como webhook secret.
/// <see cref="UsesWebhookSecret"/>: el proveedor tiene ese dato y la oficina PUEDE cargarlo; nunca es
/// obligatorio para activar (sin él se cobra igual, pero no se sincronizan reembolsos ni disputas).
/// </summary>
public sealed record PaymentProviderDescriptor(
    PaymentProviderCode Code,
    string DisplayName,
    string PublicKeyLabel,
    string SecretKeyLabel,
    bool UsesWebhookSecret,
    bool AcceptsApiBaseUrl,
    string? ApiBaseUrlHelp = null,
    string? ApiBaseUrlPlaceholder = null,
    string? CredentialsHelpUrl = null,
    IReadOnlyList<string>? WebhookEvents = null,
    string? WebhookRouteName = null,
    string? WebhookSecretLabel = null
);
