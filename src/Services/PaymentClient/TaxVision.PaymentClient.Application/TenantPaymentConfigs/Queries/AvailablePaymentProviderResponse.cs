namespace TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;

/// <summary>Un proveedor del catálogo, con lo que el formulario del CRM debe pedir. <see cref="Code"/>
/// es el mismo valor que <c>TenantPaymentConfigResponse.ProviderCode</c> y que la ruta <c>{provider}</c>.
/// <see cref="WebhookPath"/>: ruta relativa (ya con la oficina) que hay que registrar en el proveedor; la
/// completa el controller porque depende del enrutado HTTP. <see cref="WebhookSecretLabel"/>: etiqueta del
/// campo webhook secret ("Signing secret" en Stripe, "Webhook ID" en PayPal). <see cref="WebhookAutoSetup"/>:
/// el backend crea el webhook solo al guardar las claves (función encendida y el proveedor lo soporta en
/// DirectApiKeys); el CRM puede ocultar la guía manual.</summary>
public sealed record AvailablePaymentProviderResponse(
    string Code,
    string DisplayName,
    string PublicKeyLabel,
    string SecretKeyLabel,
    bool UsesWebhookSecret,
    string? WebhookSecretLabel,
    bool AcceptsApiBaseUrl,
    string? ApiBaseUrlHelp,
    string? ApiBaseUrlPlaceholder,
    string? CredentialsHelpUrl,
    IReadOnlyList<string> WebhookEvents,
    string? WebhookPath = null,
    bool WebhookAutoSetup = false
);
