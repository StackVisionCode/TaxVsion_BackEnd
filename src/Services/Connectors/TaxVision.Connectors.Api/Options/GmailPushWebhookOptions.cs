namespace TaxVision.Connectors.Api.Options;

/// <summary>Audiencia esperada del JWT OIDC que Pub/Sub firma en cada push — la URL pública del propio webhook, configurada en la suscripción de Pub/Sub. Vacía = solo se valida la firma, sin chequear audiencia (dev sin Pub/Sub real todavía).</summary>
public sealed class GmailPushWebhookOptions
{
    public const string SectionName = "Connectors:Webhooks:GmailPush";

    public string ExpectedAudience { get; set; } = string.Empty;
}
