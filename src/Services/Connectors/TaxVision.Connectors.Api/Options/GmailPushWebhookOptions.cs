using Microsoft.Extensions.Options;

namespace TaxVision.Connectors.Api.Options;

/// <summary>Audiencia esperada del JWT OIDC que Pub/Sub firma en cada push — la URL pública del propio webhook, configurada en la suscripción de Pub/Sub. Obligatoria fuera de Development (CON-12); en Development, vacía valida solo la firma.</summary>
public sealed class GmailPushWebhookOptions
{
    public const string SectionName = "Connectors:Webhooks:GmailPush";

    public string ExpectedAudience { get; set; } = string.Empty;
}

/// <summary>Sin audiencia, cualquier token firmado por Google pasaría el webhook: fuera de Development no arranca.</summary>
public sealed class GmailPushWebhookOptionsValidator(IHostEnvironment environment)
    : IValidateOptions<GmailPushWebhookOptions>
{
    public ValidateOptionsResult Validate(string? name, GmailPushWebhookOptions options) =>
        environment.IsDevelopment() || !string.IsNullOrWhiteSpace(options.ExpectedAudience)
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{GmailPushWebhookOptions.SectionName}:ExpectedAudience is required outside Development "
                    + "(env CONNECTORS_GMAIL_PUSH_AUDIENCE): it must match the audience of the Pub/Sub push subscription."
            );
}
