using System.Collections.Concurrent;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Cuenta requests entrantes por tenant (o por el bucket "anon" para tráfico sin <c>tenant_id</c>
/// resuelto — pre-auth/JWT ausente) en una ventana deslizante por-segundo, para que
/// <see cref="LoadShedder"/> priorice el shedding hacia los tenants de mayor consumo (Fase 5 del
/// plan de rate limiting). Señal local a esta réplica del Gateway, no agregada de flota — mismo
/// criterio que <see cref="RequestOutcomeWindow"/>.
/// </summary>
public sealed class TenantConsumptionTracker(int windowSeconds)
{
    public const string AnonymousKey = "anon";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, long>> perTenantBuckets = new();

    public void RecordRequest(string tenantKey)
    {
        var bucketKey = CurrentBucketKey();
        var buckets = perTenantBuckets.GetOrAdd(tenantKey, _ => new ConcurrentDictionary<long, long>());
        buckets.AddOrUpdate(bucketKey, 1, (_, count) => count + 1);
    }

    /// <summary>Top-N tenants por requests en la ventana, orden descendente. Poda de paso los
    /// buckets vencidos y las entradas de tenant que quedaron en 0.</summary>
    public IReadOnlyList<TenantConsumption> GetTopConsumers(int topN)
    {
        var cutoff = CurrentBucketKey() - windowSeconds;
        var results = new List<TenantConsumption>(perTenantBuckets.Count);

        foreach (var (tenantKey, buckets) in perTenantBuckets)
        {
            var expired = buckets.Keys.Where(key => key <= cutoff).ToArray();
            foreach (var key in expired)
                buckets.TryRemove(key, out _);

            var total = buckets.Values.Sum();
            if (total > 0)
                results.Add(new TenantConsumption(tenantKey, total));
            else
                perTenantBuckets.TryRemove(tenantKey, out _);
        }

        return results.OrderByDescending(r => r.RequestCount).Take(topN).ToArray();
    }

    private static long CurrentBucketKey() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

public readonly record struct TenantConsumption(string TenantKey, long RequestCount);
