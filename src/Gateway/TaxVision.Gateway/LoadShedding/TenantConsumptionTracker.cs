using System.Collections.Concurrent;
using System.Net;

namespace TaxVision.Gateway.LoadShedding;

/// <summary>
/// Cuenta requests entrantes por tenant en una ventana deslizante por-segundo, para que
/// <see cref="LoadShedder"/> pueda comparar a cada tenant con la media de tenants activos (Nivel 2
/// de GW-14). Señal local a esta réplica del Gateway, no agregada de flota — mismo criterio que
/// <see cref="RequestOutcomeWindow"/>.
///
/// <para>
/// El tráfico sin <c>tenant_id</c> (firmantes públicos, links compartidos, pre-auth) se cuenta por IP
/// de cliente, no como un único tenant "anon": agregado, ese bucket superaba casi siempre la parte
/// justa y en sobrecarga se descartaba a todos los usuarios públicos juntos. Anónimos y tenants son
/// dos poblaciones que solo se comparan entre sí — si las IPs anónimas (muchas y pequeñas) entraran
/// en la media de los tenants, la bajarían tanto que cualquier firma parecería abusiva.
/// </para>
/// </summary>
public sealed class TenantConsumptionTracker(int windowSeconds)
{
    public const string AnonymousKeyPrefix = "anon:";

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<long, long>> perTenantBuckets = new();

    public static string AnonymousKeyFor(IPAddress? clientIp)
    {
        if (clientIp is null)
            return AnonymousKeyPrefix + "unknown";

        return AnonymousKeyPrefix + (clientIp.IsIPv4MappedToIPv6 ? clientIp.MapToIPv4() : clientIp);
    }

    public static bool IsAnonymous(string key) => key.StartsWith(AnonymousKeyPrefix, StringComparison.Ordinal);

    public void RecordRequest(string tenantKey)
    {
        var bucketKey = CurrentBucketKey();
        var buckets = perTenantBuckets.GetOrAdd(tenantKey, _ => new ConcurrentDictionary<long, long>());
        buckets.AddOrUpdate(bucketKey, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Total de la ventana, número de claves activas y consumo de la clave preguntada, en una sola
    /// pasada y solo dentro de su población (tenants o anónimos). Poda de paso los buckets vencidos y
    /// las entradas que quedaron en 0.
    /// </summary>
    public ConsumptionSnapshot GetSnapshot(string tenantKey)
    {
        var cutoff = CurrentBucketKey() - windowSeconds;
        var anonymous = IsAnonymous(tenantKey);
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

            if (IsAnonymous(key) != anonymous)
                continue;

            total += tenantTotal;
            activeTenants++;
            if (key == tenantKey)
                forTenant = tenantTotal;
        }

        return new ConsumptionSnapshot(total, activeTenants, forTenant);
    }

    private static long CurrentBucketKey() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
}

/// <param name="TotalRequests">Requests de la población (tenants o anónimos) en la ventana.</param>
/// <param name="ActiveTenantCount">Claves de la población con al menos un request en la ventana.</param>
/// <param name="TenantRequests">Requests de la clave evaluada.</param>
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
