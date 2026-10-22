namespace BuildingBlocks.Web.Webhooks;

/// <summary>Por dónde llegó el secreto. <see cref="Query"/> es legado: queda en logs de proxies.</summary>
public enum WebhookSecretSource
{
    None,
    Bearer,
    Header,
    Query,
}
