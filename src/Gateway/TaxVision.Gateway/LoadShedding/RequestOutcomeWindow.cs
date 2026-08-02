namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Ventana deslizante (por-segundo) de latencia propia del Gateway + tasa de 5xx, usada por
/// <see cref="LoadShedder"/> para decidir sobrecarga (Fase 5 del plan de rate limiting). Una
/// instancia por proceso del Gateway — la señal de sobrecarga es local a esta réplica, no
/// agregada de flota (mismo criterio que un local overload manager: cada réplica protege su
/// propia capacidad, sin depender de un store compartido nuevo).
/// </summary>
public sealed class RequestOutcomeWindow(int windowSeconds)
{
    private const int MaxSamplesPerBucket = 4000;

    private readonly object gate = new();
    private readonly Dictionary<long, Bucket> buckets = new();

    public void Record(double latencyMs, int statusCode)
    {
        var bucketKey = CurrentBucketKey();
        lock (gate)
        {
            if (!buckets.TryGetValue(bucketKey, out var bucket))
            {
                bucket = new Bucket();
                buckets[bucketKey] = bucket;
            }

            if (bucket.Latencies.Count < MaxSamplesPerBucket)
                bucket.Latencies.Add(latencyMs);

            bucket.Total++;
            if (statusCode >= 500)
                bucket.Errors5xx++;

            PruneExpiredBuckets(bucketKey);
        }
    }

    public WindowSnapshot GetSnapshot()
    {
        var currentBucketKey = CurrentBucketKey();
        lock (gate)
        {
            PruneExpiredBuckets(currentBucketKey);

            var latencies = new List<double>();
            long total = 0;
            long errors5xx = 0;
            foreach (var bucket in buckets.Values)
            {
                latencies.AddRange(bucket.Latencies);
                total += bucket.Total;
                errors5xx += bucket.Errors5xx;
            }

            if (total == 0)
                return new WindowSnapshot(0, 0, 0);

            latencies.Sort();
            var p99Index = Math.Min(latencies.Count - 1, (int)Math.Ceiling(latencies.Count * 0.99) - 1);
            var p99 = latencies.Count > 0 ? latencies[Math.Max(0, p99Index)] : 0;

            return new WindowSnapshot(total, p99, (double)errors5xx / total);
        }
    }

    private void PruneExpiredBuckets(long currentBucketKey)
    {
        var cutoff = currentBucketKey - windowSeconds;
        var expired = buckets.Keys.Where(key => key <= cutoff).ToArray();
        foreach (var key in expired)
            buckets.Remove(key);
    }

    private static long CurrentBucketKey() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private sealed class Bucket
    {
        public List<double> Latencies { get; } = [];
        public long Total { get; set; }
        public long Errors5xx { get; set; }
    }
}

public readonly record struct WindowSnapshot(long SampleCount, double P99LatencyMs, double ErrorRate5xx);
