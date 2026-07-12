namespace TaxVision.Notification.Api.Common;

/// <summary>Secreto compartido para autenticar los webhooks de tracking de proveedores de correo.</summary>
public sealed class EmailWebhookOptions
{
    public const string SectionName = "EmailWebhook";

    public string Secret { get; set; } = string.Empty;
}
