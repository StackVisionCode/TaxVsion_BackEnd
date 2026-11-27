namespace TaxVision.Auth.Application.Abstractions;

/// <summary>Respuesta de Notification sobre el correo de un evento: sin respuesta, sin fila, o su estado.</summary>
public sealed record NotificationEmailLookup(bool Reachable, string? Status)
{
    public static NotificationEmailLookup Unavailable { get; } = new(false, null);
    public static NotificationEmailLookup NotFound { get; } = new(true, null);

    public static NotificationEmailLookup Found(string status) => new(true, status);
}

/// <summary>
/// Estado de entrega bajo demanda (AJ-08): M2M a Notification por el <c>EventId</c> del correo. Nunca lanza; si
/// Notification no responde devuelve <see cref="NotificationEmailLookup.Unavailable"/>.
/// </summary>
public interface INotificationDeliveryClient
{
    Task<NotificationEmailLookup> GetEmailStatusAsync(Guid tenantId, Guid eventId, CancellationToken ct);
}
