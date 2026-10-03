using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Infrastructure.Providers.TenantDirectory;

namespace TaxVision.Postmaster.Infrastructure.Seed;

/// <summary>
/// El recorrido del backfill del directorio, separado del <c>IHostedService</c> que lo envuelve.
///
/// <para>Está aparte para poder probarlo: arrancar el hosted service de verdad levantaría el host
/// entero contra la base real y no diría nada sobre el paginado ni la idempotencia, que es
/// exactamente donde están los fallos que importan.</para>
/// </summary>
public static class BackfillPlan
{
    /// <summary>Tope de seguridad: 200 páginas son 20.000 oficinas. Llegar ahí es un bucle, no un dato.</summary>
    private const int MaxPages = 200;

    /// <summary>Cuántas oficinas se escribieron. 0 = ya estaba completo (o Tenant no contestó).</summary>
    public static async Task<int> RunAsync(
        ITenantDirectoryClient client,
        ITenantDirectoryRepository repository,
        int pageSize,
        CancellationToken ct
    )
    {
        var known = new HashSet<Guid>(await repository.GetKnownTenantIdsAsync(ct));
        var added = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await client.GetPageAsync(page, pageSize, ct);
            if (batch.Count == 0)
                break;

            foreach (var tenant in batch)
            {
                // `known` se va llenando sobre la marcha: si el listado trajera dos veces la misma
                // oficina, el contador diría que se escribieron dos.
                if (!known.Add(tenant.Id))
                    continue;

                await repository.UpsertAsync(tenant.Id, tenant.Name, tenant.Subdomain, DateTime.UtcNow, ct);
                added++;
            }

            // Una página corta es el final. Una llena puede serlo también, así que hay que pedir la
            // siguiente para saberlo — por eso el corte va DESPUÉS de procesar, no antes.
            if (batch.Count < pageSize)
                break;
        }

        return added;
    }
}
