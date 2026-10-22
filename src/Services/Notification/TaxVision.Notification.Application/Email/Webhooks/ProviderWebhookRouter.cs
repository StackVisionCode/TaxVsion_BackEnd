using TaxVision.Notification.Application.Email.Sending.Campaign;

namespace TaxVision.Notification.Application.Email.Webhooks;

/// <summary>
/// Decide el dueño de un aviso mirando solo los ids, sin interpretar el evento. SMTP2GO en plan gratis
/// permite un único webhook: campañas y correos del sistema llegan por el mismo lado.
/// </summary>
public static class ProviderWebhookRouter
{
    /// <summary>Header que Postmaster agrega a cada envío; SMTP2GO lo devuelve si se configura en el webhook.</summary>
    public const string SystemCorrelationHeader = "X-TaxVision-Message-Id";

    /// <summary>Prefijo del Message-ID que pone Postmaster.</summary>
    private const string SystemMessageIdPrefix = "pm.";

    public static ProviderWebhookRoute Classify(string? messageId, string? systemCorrelationHeader)
    {
        if (CampaignEmailCorrelation.TryParse(messageId, out _, out _, out _, out _))
            return ProviderWebhookRoute.Campaign;

        if (!string.IsNullOrWhiteSpace(systemCorrelationHeader) || IsSystemMessageId(messageId))
            return ProviderWebhookRoute.SystemEmail;

        return ProviderWebhookRoute.Unrouted;
    }

    private static bool IsSystemMessageId(string? messageId) =>
        messageId is not null
        && messageId.Trim().TrimStart('<').StartsWith(SystemMessageIdPrefix, StringComparison.Ordinal);
}
