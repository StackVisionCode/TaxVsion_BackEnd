namespace TaxVision.Notification.Infrastructure.Retention;

public interface INotificationRetentionLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan expiry, CancellationToken ct);
}
