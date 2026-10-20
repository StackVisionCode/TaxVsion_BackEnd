using StackExchange.Redis;

namespace TaxVision.Notification.Infrastructure.Retention;

public sealed class NotificationRetentionLock(IConnectionMultiplexer redis) : INotificationRetentionLock
{
    private const string LockKey = "taxvision:notification:retention-lock";

    public async Task<IAsyncDisposable?> TryAcquireAsync(TimeSpan expiry, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var token = Guid.NewGuid().ToString("N");
        var acquired = await redis.GetDatabase().StringSetAsync(LockKey, token, expiry, When.NotExists);
        return acquired ? new Handle(redis.GetDatabase(), token) : null;
    }

    private sealed class Handle(IDatabase database, string token) : IAsyncDisposable
    {
        private const string ReleaseScript =
            "if redis.call('get', KEYS[1]) == ARGV[1] then return redis.call('del', KEYS[1]) else return 0 end";

        public async ValueTask DisposeAsync()
        {
            await database.ScriptEvaluateAsync(ReleaseScript, [LockKey], [token]);
        }
    }
}
