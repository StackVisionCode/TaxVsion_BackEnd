namespace TaxVision.Notification.Application.Email.Sending.Campaign;

/// <summary>
/// Correo de campaña ya renderizado, listo para entregar por un proveedor concreto. El From/ReplyTo los
/// decide el proveedor desde su propia config (identidad de envío de la plataforma/oficina) — este modelo
/// solo lleva el destinatario y el contenido, más ids para correlación/observabilidad.
/// </summary>
public sealed record CampaignEmailMessage(
    Guid TenantId,
    string To,
    string? ToName,
    string Subject,
    string HtmlBody,
    string? TextBody,
    Guid CampaignId,
    Guid RunId,
    string DispatchId
);

/// <summary>Desenlace del envío de UN correo de campaña por el proveedor activo.</summary>
public sealed record CampaignEmailSendResult(bool Accepted, string? ProviderMessageId, string? ErrorCode)
{
    public static CampaignEmailSendResult Ok(string? providerMessageId) => new(true, providerMessageId, null);

    public static CampaignEmailSendResult Fail(string errorCode) => new(false, null, errorCode);
}

/// <summary>
/// Puerto del ejecutor de canal <b>Email</b> de Campaigns, <b>independiente de Postmaster</b> y
/// <b>agnóstico de proveedor</b>: el adapter concreto (SMTP2GO hoy; SES/SendGrid/otro SMTP a futuro) se
/// elige por config (<c>CampaignEmail:Provider</c>) sin tocar el consumer. Cambiar de proveedor = cambiar
/// esa config. Mismo espíritu pluggable que <c>ISmsProvider</c> en TaxVision.Sms.
/// </summary>
public interface ICampaignEmailProvider
{
    /// <summary>Código del proveedor activo (p. ej. "smtp2go"), para logs/observabilidad.</summary>
    string Code { get; }

    Task<CampaignEmailSendResult> SendAsync(CampaignEmailMessage message, CancellationToken ct = default);
}
