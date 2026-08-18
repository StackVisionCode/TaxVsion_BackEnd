using System.Collections.Concurrent;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Cuenta requests entrantes por tenant (o por el bucket "anon" para tráfico sin <c>tenant_id</c>
/// resuelto — pre-auth/JWT ausente) en una ventana deslizante por-segundo, para que
/// <see cref="LoadShedder"/> pueda comparar a cada tenant con la media de tenants activos (Nivel 2
/// de GW-14). Señal local a esta réplica del Gateway, no agregada de flota — mismo criterio que
/// <see cref="RequestOutcomeWindow"/>.
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

    /// <summary>
    /// Total de la ventana, número de tenants activos y consumo del tenant preguntado, en una sola
    /// pasada. Poda de paso los buckets vencidos y las entradas de tenant que quedaron en 0.
    /// </summary>
    public ConsumptionSnapshot GetSnapshot(string tenantKey)
    {
        var cutoff = CurrentBucketKey() - windowSeconds;
        long total = 0;
        var activeTenants = 0;
        long forTenant = 0;

        foreach (var (key, buckets) in perTenantBuckets)
        {
            foreach (var expired in buckets.Keys.Where(k => k <= cutoff).ToArray())
                buckets.TryRemove(expired, out _);

            var tenantTotal = buckets.Values.Sum();
            if (tenantTotal == 0)
            {
                perTenantBuckets.TryRemove(key, out _);
                continue;
            }

            total += tenantTotal;
            activeTenants++;
            if (key == tenantKey)
                forTenant = tenantTotal;
        }

        return new ConsumptionSnapshot(total, activeTenants, forTenant);
    }

    private static long CurrentBucketKey() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

/// <param name="TotalRequests">Requests de todos los tenants en la ventana.</param>
/// <param name="ActiveTenantCount">Tenants con al menos un request en la ventana.</param>
/// <param name="TenantRequests">Requests del tenant evaluado.</param>
public readonly record struct ConsumptionSnapshot(long TotalRequests, int ActiveTenantCount, long TenantRequests)
{
    /// <summary>
    /// Cuántas veces por encima de la media de tenants activos está este tenant. <c>0</c> cuando no
    /// hay tráfico: sin muestras no hay exceso que medir, y devolver <c>1</c> (la media exacta)
    /// mentiría igual pero sería más difícil de leer en un log.
    /// </summary>
    public double ExcessOverFairShare =>
        ActiveTenantCount == 0 || TotalRequests == 0 ? 0 : TenantRequests / ((double)TotalRequests / ActiveTenantCount);
}
