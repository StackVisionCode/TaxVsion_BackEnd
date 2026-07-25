using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF implementation de <see cref="INotificationLogQueryRepository"/> — carga el aggregate con la
/// colección de attempts tracked para que los consumers de callback puedan invocar
/// <c>UpdateAttemptStatus</c> y guardar en el mismo scope.
/// </summary>
public sealed class NotificationLogQueryRepository(NotificationDbContext dbContext) : INotificationLogQueryRepository
{
    // IgnoreQueryFilters(): los 5 consumers de callback de Postmaster (Succeeded/Failed/Bounced/
    // Suppressed/ProviderNotConfigured) corren dentro de scope de DI de Wolverine (ITenantContext
    // vacío). El notificationLogId viene del propio evento de callback, no adivinable (fue generado
    // en el envío original). Sin esto, TODOS los callbacks de Postmaster caían en el "log
    // desconocido; dropping" fallback y los NotificationLog quedaban Pending para siempre.
    public Task<NotificationLog?> FindWithAttemptsAsync(Guid notificationLogId, CancellationToken ct) =>
        dbContext
            .NotificationLogs.IgnoreQueryFilters()
            .Include(l => l.Attempts)
            .FirstOrDefaultAsync(l => l.Id == notificationLogId, ct);
}
