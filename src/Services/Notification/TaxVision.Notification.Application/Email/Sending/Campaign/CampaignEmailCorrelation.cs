namespace TaxVision.Notification.Application.Email.Sending.Campaign;

/// <summary>
/// Correlación proveedor↔campaña codificada en el <b>Message-Id</b> del correo. El adapter lo fija al
/// enviar; el webhook del proveedor (SMTP2GO devuelve el Message-Id del remitente) lo decodifica para
/// publicar el <c>CampaignDispatchResult</c> de Delivered/Failed sin necesitar ninguna tabla de mapeo.
///
/// <para>Formato del local-part: <c>{tenant:N}.{campaign:N}.{run:N}.{dispatchId}</c>; el <c>dispatchId</c>
/// va último porque puede traer guiones (p. ej. "...-1") pero nunca puntos. Dominio fijo
/// <see cref="Domain"/> solo para formar un Message-Id válido.</para>
/// </summary>
public static class CampaignEmailCorrelation
{
    public const string Domain = "cmp.taxproffice.local";

    public static string BuildMessageId(Guid tenantId, Guid campaignId, Guid runId, string dispatchId) =>
        $"{tenantId:N}.{campaignId:N}.{runId:N}.{dispatchId}@{Domain}";

    /// <summary>Decodifica un Message-Id (con o sin &lt;&gt;) de vuelta a sus ids. false si no es uno nuestro.</summary>
    public static bool TryParse(
        string? messageId,
        out Guid tenantId,
        out Guid campaignId,
        out Guid runId,
        out string dispatchId
    )
    {
        tenantId = campaignId = runId = Guid.Empty;
        dispatchId = string.Empty;
        if (string.IsNullOrWhiteSpace(messageId))
            return false;

        var trimmed = messageId.Trim().Trim('<', '>').Trim();
        var at = trimmed.IndexOf('@');
        var local = at >= 0 ? trimmed[..at] : trimmed;

        var parts = local.Split('.', 4);
        if (parts.Length != 4)
            return false;

        if (
            !Guid.TryParseExact(parts[0], "N", out tenantId)
            || !Guid.TryParseExact(parts[1], "N", out campaignId)
            || !Guid.TryParseExact(parts[2], "N", out runId)
            || string.IsNullOrWhiteSpace(parts[3])
        )
            return false;

        dispatchId = parts[3];
        return true;
    }
}
