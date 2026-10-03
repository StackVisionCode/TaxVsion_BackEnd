using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Infrastructure.Providers.TenantDirectory;

namespace TaxVision.Scribe.Infrastructure.Seed;

/// <summary>
/// El recorrido del backfill, separado del <c>IHostedService</c> que lo envuelve para poder probarlo
/// sin levantar el host contra la base real.
/// </summary>
public static class TenantProfileBackfillPlan
{
    /// <summary>Tope de seguridad: 200 paginas son 20.000 oficinas. Llegar ahi es un bucle, no un dato.</summary>
    private const int MaxPages = 200;

    /// <summary>
    /// <paramref name="TenantReachable"/> separa "ya estaba completo" de "no se pudo ni preguntar":
    /// las dos escriben 0 filas y confundirlas deja un log que miente.
    /// </summary>
    public readonly record struct BackfillOutcome(int Added, bool TenantReachable);

    public static async Task<BackfillOutcome> RunAsync(
        ITenantDirectoryClient client,
        ITenantProfileRefRepository repository,
        int pageSize,
        CancellationToken ct
    )
    {
        var known = new HashSet<Guid>(await repository.GetKnownTenantIdsAsync(ct));
        var added = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await client.GetPageAsync(page, pageSize, ct);
            if (batch is null)
                return new BackfillOutcome(added, TenantReachable: false);

            if (batch.Count == 0)
                break;

            foreach (var tenant in batch)
            {
                // `known` se llena sobre la marcha: si el listado repitiera una oficina, el contador mentiria.
                if (!known.Add(tenant.Id))
                    continue;

                await repository.UpsertAsync(tenant.Id, tenant.Name, tenant.Subdomain, DateTime.UtcNow, ct);
                added++;
            }

            // Una pagina corta es el final. Una llena puede serlo tambien, por eso el corte va DESPUES.
            if (batch.Count < pageSize)
                break;
        }

        return new BackfillOutcome(added, TenantReachable: true);
    }
}
