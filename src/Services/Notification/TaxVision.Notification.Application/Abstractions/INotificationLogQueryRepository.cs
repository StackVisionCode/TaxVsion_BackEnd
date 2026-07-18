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
    Task<NotificationLog?> FindWithAttemptsAsync(Guid notificationLogId, CancellationToken ct);
}
