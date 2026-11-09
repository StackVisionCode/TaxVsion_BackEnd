using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Contrato para cargar un <see cref="NotificationLog"/> con sus <see cref="NotificationDispatchAttempt"/>
/// tracked, específico para los consumers de callback de Postmaster (Notifications Fase 4).
/// Separado de <see cref="INotificationLogRepository"/> — ese último es agnóstico de EF y solo agrega/pagina;
/// este necesita include explícito de la colección de attempts.
/// </summary>
public interface INotificationLogQueryRepository
{
    /// <summary>El log solo se devuelve si es del tenant del callback.</summary>
    Task<NotificationLog?> FindWithAttemptsAsync(Guid tenantId, Guid notificationLogId, CancellationToken ct);

    /// <summary>Existe con cualquier tenant: distingue un callback de otro tenant de un id desconocido.</summary>
    Task<bool> ExistsAsync(Guid notificationLogId, CancellationToken ct);
}
