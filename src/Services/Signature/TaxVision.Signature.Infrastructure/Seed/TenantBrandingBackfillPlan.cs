using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Seed;

/// <summary>
/// El recorrido del backfill de <see cref="TenantBrandingRef"/>, separado del <c>IHostedService</c> que lo
/// envuelve para poder probar el paginado y la idempotencia sin levantar el host contra la base real.
/// </summary>
public static class TenantBrandingBackfillPlan
{
    /// <summary>Tope de seguridad: 200 páginas son 20.000 oficinas. Llegar ahí es un bucle, no un dato.</summary>
    private const int MaxPages = 200;

    /// <summary>
    /// Qué pasó. <paramref name="TenantReachable"/> separa "ya estaba completo" de "no se pudo ni
    /// preguntar": las dos escriben 0 filas y confundirlas deja un log que miente.
    /// </summary>
    public readonly record struct BackfillOutcome(int Added, int Completed, bool TenantReachable)
    {
        public int Changed => Added + Completed;
    }

    public static async Task<BackfillOutcome> RunAsync(
        ITenantDirectoryClient client,
        ITenantBrandingRefRepository repository,
        int pageSize,
        CancellationToken ct
    )
    {
        var known = (await repository.ListAllAsync(ct)).ToDictionary(r => r.TenantId);
        var added = 0;
        var completed = 0;

        for (var page = 1; page <= MaxPages; page++)
        {
            var batch = await client.GetPageAsync(page, pageSize, ct);
            if (batch is null)
                return new BackfillOutcome(added, completed, TenantReachable: false);

            if (batch.Count == 0)
                break;

            var now = DateTime.UtcNow;
            foreach (var tenant in batch)
            {
                if (known.TryGetValue(tenant.Id, out var existing))
                {
                    // Fila creada por el evento del logo (sin nombre) o anterior a la columna SubDomain.
                    if (existing.FillMissingIdentity(tenant.Name, tenant.Subdomain, now))
                        completed++;
                    continue;
                }

                var branding = TenantBrandingRef.Create(tenant.Id, now);
                branding.SetOfficeName(tenant.Name, now);
                branding.SetSubDomain(tenant.Subdomain, now);
                await repository.AddAsync(branding, ct);
                // `known` se llena sobre la marcha: si el listado trajera dos veces la misma oficina, no
                // se insertaría dos veces.
                known[tenant.Id] = branding;
                added++;
            }

            // Una página corta es el final. Una llena puede serlo también, así que hay que pedir la
            // siguiente para saberlo; por eso el corte va DESPUÉS de procesar, no antes.
            if (batch.Count < pageSize)
                break;
        }

        return new BackfillOutcome(added, completed, TenantReachable: true);
    }
}
