using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A5 — aislamiento fail-closed: un TenantDomain de otro tenant nunca se revela
/// (ni siquiera como 403, que confirmaría que existe) — siempre TenantDomain.NotFound.
/// Único punto de esta comprobación para verify/activate/disable.
/// </summary>
internal static class TenantDomainAccessGuard
{
    public static async Task<Result<TenantDomain>> LoadOwnedAsync(
        ITenantDomainRepository domains,
        Guid tenantId,
        Guid domainId,
        CancellationToken ct
    )
    {
        var domain = await domains.GetByIdAsync(domainId, ct);
        return domain is null || domain.TenantId != tenantId
            ? Result.Failure<TenantDomain>(new Error("TenantDomain.NotFound", "Tenant domain not found."))
            : Result.Success(domain);
    }
}
