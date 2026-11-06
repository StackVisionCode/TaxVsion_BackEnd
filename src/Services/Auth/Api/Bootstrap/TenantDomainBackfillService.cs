using BuildingBlocks.Infrastructure.Hosting;
using BuildingBlocks.Jobs;
using BuildingBlocks.Platform;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Api.Bootstrap;

/// <summary>
/// Backfill de arranque (Fase A3): crea el TenantDomain primario para cualquier tenant
/// que exista desde antes de que la tabla TenantDomains existiera (Fase A2) y por lo
/// tanto no tenga ninguno. Sin este backfill, habilitar "host desconocido -> 404" en
/// TenantHostResolutionMiddleware rompería el login de todos los tenants pre-existentes.
/// Idempotente: no hace nada en corridas posteriores una vez que todos los tenants
/// tienen su dominio primario. Los tenants creados a partir de ahora ya reciben el
/// suyo directamente en TenantCreatedConsumer.
///
/// Antes del backfill re-apunta las filas existentes al dominio de la plataforma
/// (<see cref="ITenantDomainRebaser"/>). Si después llega una versión nueva de PlatformSettings, lo
/// repite el consumer del evento.
/// </summary>
public sealed class TenantDomainBackfillService(
    IServiceScopeFactory scopeFactory,
    IPlatformSettings platform,
    IHostApplicationLifetime lifetime,
    ILogger<TenantDomainBackfillService> logger
) : DeferredStartupHostedService(lifetime, logger)
{
    protected override async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var settings = platform.Current;
        List<(Guid Id, string SubDomain)> tenantsWithoutPrimaryDomain;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            await scope
                .ServiceProvider.GetRequiredService<ITenantDomainRebaser>()
                .RebaseAsync(settings.BaseDomain, settings.ApiHost, cancellationToken);

            // RBAC Fase 5 — este backfill recorre TODOS los tenants en una sola query correlacionada
            // (Tenant no es ITenantOwned, no lo afecta el filtro; TenantDomain sí lo es — la subquery
            // necesita IgnoreQueryFilters() explícito porque compara contra el TenantId de CADA fila
            // de Tenants, no contra un tenant ambiente único).
            tenantsWithoutPrimaryDomain = (
                await db
                    .Tenants.Where(tenant =>
                        tenant.Kind == TenantKind.Customer
                        && !db
                            .TenantDomains.IgnoreQueryFilters()
                            .Any(domain => domain.TenantId == tenant.Id && domain.IsPrimary)
                    )
                    .Select(tenant => new { tenant.Id, tenant.SubDomain })
                    .ToListAsync(cancellationToken)
            )
                .Select(tenant => (tenant.Id, tenant.SubDomain))
                .ToList();
        }

        if (tenantsWithoutPrimaryDomain.Count == 0)
            return;

        // Una unidad por tenant: el dominio y su evento de dominio (auditoría + aviso) se confirman juntos.
        var summary = await TransactionalJobUnit.RunEachAsync<(Guid Id, string SubDomain), AuthDbContext>(
            scopeFactory,
            tenantsWithoutPrimaryDomain,
            (tenant, unit, token) => CreatePrimaryDomainAsync(tenant.Id, tenant.SubDomain, settings, unit, token),
            logger,
            cancellationToken
        );

        if (summary.Committed > 0)
        {
            logger.LogInformation(
                "Backfill: created {Created} primary TenantDomain row(s) for pre-existing tenants ({Skipped} skipped).",
                summary.Committed,
                tenantsWithoutPrimaryDomain.Count - summary.Committed
            );
        }
    }

    private async Task<bool> CreatePrimaryDomainAsync(
        Guid tenantId,
        string subDomain,
        PlatformSettingsSnapshot settings,
        JobUnitContext<AuthDbContext> unit,
        CancellationToken ct
    )
    {
        var slugResult = SubdomainSlug.Create(subDomain);
        if (slugResult.IsFailure)
        {
            logger.LogWarning(
                "Backfill: tenant {TenantId} subdomain {SubDomain} is not a valid TenantDomain slug ({Error}); "
                    + "skipping. Requires manual remediation.",
                tenantId,
                subDomain,
                slugResult.Error.Code
            );
            return false;
        }

        var db = unit.DbContext;
        var host = settings.TenantHost(slugResult.Value.Value);
        // RBAC Fase 5 — unicidad de host es GLOBAL (cruza tenants a propósito).
        if (await db.TenantDomains.IgnoreQueryFilters().AnyAsync(domain => domain.Host == host, ct))
        {
            logger.LogWarning(
                "Backfill: host {Host} for tenant {TenantId} already claimed by another TenantDomain row; "
                    + "skipping. Requires manual remediation.",
                host,
                tenantId
            );
            return false;
        }

        var domainResult = TenantDomain.CreateSubdomain(
            tenantId,
            slugResult.Value,
            settings.BaseDomain,
            createdByUserId: Guid.Empty,
            DateTime.UtcNow
        );
        if (domainResult.IsFailure)
        {
            logger.LogWarning(
                "Backfill: failed to create primary TenantDomain for tenant {TenantId}: {Error}",
                tenantId,
                domainResult.Error.Code
            );
            return false;
        }

        await db.TenantDomains.AddAsync(domainResult.Value, ct);
        return true;
    }
}
