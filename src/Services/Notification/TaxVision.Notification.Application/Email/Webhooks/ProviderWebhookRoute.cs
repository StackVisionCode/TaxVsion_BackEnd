namespace TaxVision.Notification.Application.Email.Webhooks;

/// <summary>A quién le toca un aviso del webhook único del proveedor.</summary>
public enum ProviderWebhookRoute
{
    /// <summary>Correo de campaña: lo resuelve Campaigns.</summary>
    Campaign,

    /// <summary>Correo del sistema enviado por Postmaster: se le reenvía crudo.</summary>
    SystemEmail,

    /// <summary>Otro tráfico de la misma cuenta del proveedor: se ignora.</summary>
    Unrouted,
}
