namespace TaxVision.Notification.Infrastructure.Email.Campaign;

/// <summary>
/// Config del ejecutor de email de campaña (sección <c>CampaignEmail</c>). <see cref="Provider"/> elige el
/// adapter activo (hoy "smtp2go"/"smtp" → <c>SmtpCampaignEmailProvider</c>); agregar otro proveedor es
/// sumar su adapter + su bloque de config, sin tocar el consumer. Independiente de Postmaster.
/// </summary>
public sealed class CampaignEmailOptions
{
    public const string SectionName = "CampaignEmail";

    /// <summary>Código del proveedor activo: "smtp2go"/"smtp" (SMTP directo) o "smtp2go-api"/"api"
    /// (API HTTP de SMTP2GO, recomendado para volumen — sin handshake por correo).</summary>
    public string Provider { get; init; } = "smtp2go";

    /// <summary>Tope de correos por minuto del ejecutor de campaña (throttle compartido, respeta el
    /// límite de la cuenta del proveedor). 0 = sin límite. Pacea de forma pareja entre envíos paralelos.</summary>
    public int MaxPerMinute { get; init; }

    /// <summary>Config del transporte SMTP (usada por el adapter SMTP, p. ej. SMTP2GO).</summary>
    public SmtpOptions Smtp { get; init; } = new();

    /// <summary>Config de la API HTTP de SMTP2GO (usada por el adapter API).</summary>
    public ApiOptions Api { get; init; } = new();

    /// <summary>
    /// Secreto compartido opcional para el webhook de estado del proveedor (header <c>X-Webhook-Secret</c>
    /// o query <c>?key=</c>). Vacío ⇒ no se exige (dev). En prod, configurarlo y ponerlo en el webhook del panel.
    /// </summary>
    public string? WebhookSecret { get; init; }

    public sealed class SmtpOptions
    {
        public string? Host { get; init; }
        public int Port { get; init; } = 2525;
        public bool UseTls { get; init; } = true;
        public string? Username { get; init; }
        public string? Password { get; init; }
        public string? FromAddress { get; init; }
        public string? FromName { get; init; }
    }

    public sealed class ApiOptions
    {
        public string BaseUrl { get; init; } = "https://api.smtp2go.com/v3/";
        public string? ApiKey { get; init; }
        public string? FromAddress { get; init; }
        public string? FromName { get; init; }
    }
}
