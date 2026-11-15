using BuildingBlocks.Persistence;
using BuildingBlocks.TimeZones;

namespace BuildingBlocks.TenantTimeZones;

/// <summary>Resultado de una pasada: si Tenant respondió hasta el final y cuántas oficinas se corrigieron.</summary>
public sealed record TenantTimeZoneReconciliationOutcome(bool TenantReachable, int Applied);

/// <summary>
/// Repara la proyección con el directorio de Tenant (el dueño de la zona): agrega las oficinas que faltan y
/// corrige las que se perdieron un cambio. Idempotente: sin diferencias no escribe nada. Guarda por página.
/// </summary>
public static class TenantTimeZoneReconciler
{
    public static async Task<TenantTimeZoneReconciliationOutcome> RunAsync(
        ITenantDirectoryZoneClient client,
        ITenantTimeZoneStore store,
        ITenantTimeZoneResolver resolver,
        IUnitOfWork unitOfWork,
        int pageSize,
        DateTime readAtUtc,
        CancellationToken ct
    )
    {
        var applied = 0;
        for (var page = 1; ; page++)
        {
            var offices = await client.ListPageAsync(page, pageSize, ct);
            if (offices is null)
                return new TenantTimeZoneReconciliationOutcome(false, applied);

            var changed = new List<Guid>();
            foreach (var office in offices)
            {
                if (!IanaTimeZone.TryNormalize(office.TimeZoneId, out var zone))
                    continue;

                var current = await store.FindAsync(office.TenantId, ct);
                if (current is null)
                    await store.AddAsync(TenantTimeZoneProjection.Create(office.TenantId, zone, null, readAtUtc), ct);
                else if (!string.Equals(current.TimeZoneId, zone, StringComparison.Ordinal))
                    current.Reconcile(zone, readAtUtc);
                else
                    continue;

                changed.Add(office.TenantId);
            }

            if (changed.Count > 0)
            {
                await unitOfWork.SaveChangesAsync(ct);
                changed.ForEach(resolver.Forget);
                applied += changed.Count;
            }

            if (offices.Count < pageSize)
                return new TenantTimeZoneReconciliationOutcome(true, applied);
        }
    }
}
