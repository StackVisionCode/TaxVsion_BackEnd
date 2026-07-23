using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Bootstrap;

/// <summary>
/// Backfill de arranque (Fase A3): crea el TenantDomain primario para cualquier tenant
/// que exista desde antes de que la tabla TenantDomains existiera (Fase A2) y por lo
/// tanto no tenga ninguno. Sin este backfill, habilitar "host desconocido -> 404" en
/// TenantResolutionMiddleware rompería el login de todos los tenants pre-existentes.
/// Idempotente: no hace nada en corridas posteriores una vez que todos los tenants
/// tienen su dominio primario. Los tenants creados a partir de ahora ya reciben el
/// suyo directamente en TenantCreatedConsumer.
/// </summary>
public sealed class TenantDomainBackfillService(
    IServiceScopeFactory scopeFactory,
    IOptions<TenantDomainOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<TenantDomainBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    private readonly TenantDomainOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

        // RBAC Fase 5 — este backfill recorre TODOS los tenants en una sola query correlacionada
        // (Tenant no es ITenantOwned, no lo afecta el filtro; TenantDomain sí lo es — la subquery
        // necesita IgnoreQueryFilters() explícito porque compara contra el TenantId de CADA fila
        // de Tenants, no contra un tenant ambiente único).
        var tenantsWithoutPrimaryDomain = await db
            .Tenants.Where(tenant =>
                tenant.Kind == TenantKind.Customer
                && !db
                    .TenantDomains.IgnoreQueryFilters()
                    .Any(domain => domain.TenantId == tenant.Id && domain.IsPrimary)
            )
            .ToListAsync(cancellationToken);

        if (tenantsWithoutPrimaryDomain.Count == 0)
            return;

        var created = 0;
        foreach (var tenant in tenantsWithoutPrimaryDomain)
        {
            var slugResult = SubdomainSlug.Create(tenant.SubDomain);
            if (slugResult.IsFailure)
            {
                logger.LogWarning(
                    "Backfill: tenant {TenantId} subdomain {SubDomain} is not a valid TenantDomain slug ({Error}); "
                        + "skipping. Requires manual remediation.",
                    tenant.Id,
                    tenant.SubDomain,
                    slugResult.Error.Code
                );
                continue;
            }

            var host = $"{slugResult.Value.Value}.{_options.BaseDomain}";
            // RBAC Fase 5 — unicidad de host es GLOBAL (cruza tenants a propósito).
            if (await db.TenantDomains.IgnoreQueryFilters().AnyAsync(domain => domain.Host == host, cancellationToken))
            {
                logger.LogWarning(
                    "Backfill: host {Host} for tenant {TenantId} already claimed by another TenantDomain row; "
                        + "skipping. Requires manual remediation.",
                    host,
                    tenant.Id
                );
                continue;
            }

            var domainResult = TenantDomain.CreateSubdomain(
                tenant.Id,
                slugResult.Value,
                _options.BaseDomain,
                createdByUserId: Guid.Empty,
                DateTime.UtcNow
            );
            if (domainResult.IsFailure)
            {
                logger.LogWarning(
                    "Backfill: failed to create primary TenantDomain for tenant {TenantId}: {Error}",
                    tenant.Id,
                    domainResult.Error.Code
                );
                continue;
            }

            await db.TenantDomains.AddAsync(domainResult.Value, cancellationToken);
            created++;
        }

        if (created > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Backfill: created {Created} primary TenantDomain row(s) for pre-existing tenants ({Skipped} skipped).",
                created,
                tenantsWithoutPrimaryDomain.Count - created
            );
        }
    }
}
