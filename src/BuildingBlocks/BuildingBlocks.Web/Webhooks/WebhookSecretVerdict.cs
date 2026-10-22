namespace BuildingBlocks.Web.Webhooks;

public enum WebhookSecretVerdict
{
    Accepted,
    Rejected,

    /// <summary>No hay secreto configurado: se responde 503 (fail-closed), nunca se deja pasar.</summary>
    NotConfigured,
}
