using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Sin base: aplica el rastro del push sobre el mismo log, en el repositorio de la prueba.</summary>
public sealed class InlinePushAttemptRecorder(INotificationLogRepository? logs = null) : IPushAttemptRecorder
{
    private readonly Dictionary<Guid, NotificationLog> _attempts = [];

    public async Task RecordAttemptAsync(NotificationLog log, CancellationToken ct = default)
    {
        _attempts[log.Id] = log;
        if (logs is not null)
            await logs.AddAsync(log, ct);
    }

    public Task RecordSentAsync(Guid tenantId, Guid logId, CancellationToken ct = default)
    {
        _attempts[logId].MarkSent();
        return Task.CompletedTask;
    }

    public Task RecordFailedAsync(Guid tenantId, Guid logId, string error, CancellationToken ct = default)
    {
        _attempts[logId].MarkFailed(error);
        return Task.CompletedTask;
    }
}
