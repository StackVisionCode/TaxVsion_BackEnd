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
    public Task<NotificationLog?> FindWithAttemptsAsync(Guid notificationLogId, CancellationToken ct) =>
        dbContext.NotificationLogs.Include(l => l.Attempts).FirstOrDefaultAsync(l => l.Id == notificationLogId, ct);
}
