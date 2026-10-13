using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Tenancy;

/// <summary>
/// Barrido cross-tenant deliberado (corre al arrancar y al recibir el cambio de dominio, sin tenant
/// ambiental): por eso todas las consultas llevan <c>IgnoreQueryFilters()</c>.
/// </summary>
public sealed class TenantDomainRebaser(AuthDbContext db, ILogger<TenantDomainRebaser> logger) : ITenantDomainRebaser
{
    public async Task<int> RebaseAsync(string baseDomain, string apiHost, CancellationToken ct = default)
    {
        var domain = baseDomain.Trim().TrimEnd('.').ToLowerInvariant();
        if (domain.Length == 0)
            return 0;

        var suffix = "." + domain;
        var staleSubdomains = await db
            .TenantDomains.IgnoreQueryFilters()
            .Where(row => row.DomainType == TenantDomainType.Subdomain && !row.Host.EndsWith(suffix))
            .ToListAsync(ct);

        var rebased = 0;
        foreach (var row in staleSubdomains)
        {
            var previousHost = row.Host;
            if (!row.RebaseOnPlatformDomain(domain))
                continue;

            if (await IsHostClaimedAsync(row, ct))
            {
                logger.LogWarning(
                    "Rebase: host {Host} for tenant {TenantId} is already claimed; keeping {PreviousHost}. "
                        + "Requires manual remediation.",
                    row.Host,
                    row.TenantId,
                    previousHost
                );
                await db.Entry(row).ReloadAsync(ct);
                continue;
            }

            rebased++;
        }

        if (!string.IsNullOrWhiteSpace(apiHost))
        {
            var platformRow = await db
                .TenantDomains.IgnoreQueryFilters()
                .FirstOrDefaultAsync(
                    row =>
                        row.TenantId == PlatformTenant.Id
                        && row.IsPrimary
                        && row.DomainType == TenantDomainType.CustomHostname,
                    ct
                );
            if (platformRow is not null && platformRow.RebasePlatformHost(apiHost))
                rebased++;
        }

        if (rebased == 0)
            return 0;

        await db.SaveChangesAsync(ct);
        logger.LogInformation(
            "Rebase: moved {Rebased} TenantDomain row(s) to the platform domain {BaseDomain}.",
            rebased,
            domain
        );
        return rebased;
    }

    private Task<bool> IsHostClaimedAsync(TenantDomain row, CancellationToken ct) =>
        db.TenantDomains.IgnoreQueryFilters().AnyAsync(other => other.Id != row.Id && other.Host == row.Host, ct);
}
