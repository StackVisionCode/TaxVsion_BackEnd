using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Persistence;

namespace TaxVision.Notification.Infrastructure.Notifications;

/// <summary>Cada llamada usa su propio scope y conexión: confirma aunque el consumer siga con la transacción abierta.</summary>
internal sealed class PushAttemptRecorder(IServiceScopeFactory scopes) : IPushAttemptRecorder
{
    public async Task RecordAttemptAsync(NotificationLog log, CancellationToken ct = default)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        db.NotificationLogs.Add(log);
        await db.SaveChangesAsync(ct);
    }

    public Task RecordSentAsync(Guid tenantId, Guid logId, CancellationToken ct = default) =>
        UpdateAsync(tenantId, logId, log => log.MarkSent(), ct);

    public Task RecordFailedAsync(Guid tenantId, Guid logId, string error, CancellationToken ct = default) =>
        UpdateAsync(tenantId, logId, log => log.MarkFailed(error), ct);

    private async Task UpdateAsync(Guid tenantId, Guid logId, Action<NotificationLog> apply, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationDbContext>();
        var log = await db
            .NotificationLogs.IgnoreQueryFilters()
            .FirstAsync(x => x.TenantId == tenantId && x.Id == logId, ct);
        apply(log);
        await db.SaveChangesAsync(ct);
    }
}
