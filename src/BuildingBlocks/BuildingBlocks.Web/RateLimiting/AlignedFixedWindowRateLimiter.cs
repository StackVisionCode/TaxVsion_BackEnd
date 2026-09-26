using System.Threading.RateLimiting;

namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Ventana fija alineada al reloj (la ventana N va de N·Window a (N+1)·Window), igual que los
/// contadores Redis del evaluador tiered. Reemplaza al <see cref="FixedWindowRateLimiter"/> de .NET,
/// cuyo lease rechazado informa siempre la ventana entera como <c>RetryAfter</c> (no sabe cuándo empezó
/// la actual): el 429 decía "60 seconds" aunque faltaran 5. Acá la espera es exactamente lo que falta
/// para el borde. Sin cola: <see cref="FixedWindowRateLimiterOptions.QueueLimit"/> tiene que ser 0.
/// <see cref="FixedWindowRateLimiterOptions.AutoReplenishment"/> no aplica: la ventana se renueva sola
/// al pasar el borde, sin timer.
/// </summary>
public sealed class AlignedFixedWindowRateLimiter : RateLimiter
{
    private readonly int _permitLimit;
    private readonly long _windowTicks;
    private readonly TimeProvider _time;
    private readonly object _gate = new();

    private long _windowIndex = long.MinValue;
    private int _used;
    private long _lastActivityTimestamp;
    private long _successfulLeases;
    private long _failedLeases;

    public AlignedFixedWindowRateLimiter(FixedWindowRateLimiterOptions options, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.PermitLimit, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(options.Window, TimeSpan.Zero);
        if (options.QueueLimit != 0)
            throw new NotSupportedException(
                "AlignedFixedWindowRateLimiter does not queue requests: QueueLimit must be 0."
            );

        _permitLimit = options.PermitLimit;
        _windowTicks = options.Window.Ticks;
        _time = timeProvider ?? TimeProvider.System;
        _lastActivityTimestamp = _time.GetTimestamp();
    }

    public override TimeSpan? IdleDuration
    {
        get
        {
            lock (_gate)
            {
                Roll(_time.GetUtcNow().UtcTicks);
                return _used == 0 ? _time.GetElapsedTime(_lastActivityTimestamp) : null;
            }
        }
    }

    public override RateLimiterStatistics? GetStatistics()
    {
        lock (_gate)
        {
            Roll(_time.GetUtcNow().UtcTicks);
            return new RateLimiterStatistics
            {
                CurrentAvailablePermits = _permitLimit - _used,
                CurrentQueuedCount = 0,
                TotalSuccessfulLeases = _successfulLeases,
                TotalFailedLeases = _failedLeases,
            };
        }
    }

    protected override RateLimitLease AttemptAcquireCore(int permitCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(permitCount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(permitCount, _permitLimit);

        lock (_gate)
        {
            var nowTicks = _time.GetUtcNow().UtcTicks;
            Roll(nowTicks);
            _lastActivityTimestamp = _time.GetTimestamp();

            if (_used + permitCount <= _permitLimit)
            {
                _used += permitCount;
                _successfulLeases++;
                return Lease.Acquired;
            }

            // Un rechazo no consume cupo: el contador queda como estaba.
            _failedLeases++;
            var windowEndTicks = (_windowIndex + 1) * _windowTicks;
            return new Lease(acquired: false, TimeSpan.FromTicks(windowEndTicks - nowTicks));
        }
    }

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(AttemptAcquireCore(permitCount));
    }

    private void Roll(long nowTicks)
    {
        var index = nowTicks / _windowTicks;
        if (index == _windowIndex)
            return;
        _windowIndex = index;
        _used = 0;
    }

    private sealed class Lease(bool acquired, TimeSpan? retryAfter) : RateLimitLease
    {
        public static readonly Lease Acquired = new(acquired: true, retryAfter: null);

        public override bool IsAcquired => acquired;

        public override IEnumerable<string> MetadataNames => retryAfter is null ? [] : [MetadataName.RetryAfter.Name];

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            if (retryAfter is not null && metadataName == MetadataName.RetryAfter.Name)
            {
                metadata = retryAfter.Value;
                return true;
            }

            metadata = null;
            return false;
        }
    }
}

/// <summary>
/// Mismo uso que <see cref="RateLimitPartition.GetFixedWindowLimiter{TKey}"/>, pero con
/// <see cref="AlignedFixedWindowRateLimiter"/>: el 429 informa la espera real. La fitness function
/// <c>Native_limiters_report_the_real_wait</c> prohíbe el de .NET en los servicios.
/// </summary>
public static class TaxVisionRateLimitPartition
{
    public static RateLimitPartition<TKey> GetFixedWindowLimiter<TKey>(
        TKey partitionKey,
        Func<TKey, FixedWindowRateLimiterOptions> factory
    ) => RateLimitPartition.Get(partitionKey, key => new AlignedFixedWindowRateLimiter(factory(key)));
}
