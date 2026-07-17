using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.TenantDomains.Queries;

/// <summary>Fase A5 — lista de dominios (subdominio + custom hostnames) del tenant autenticado.</summary>
public sealed record GetTenantDomainsQuery(Guid TenantId);

public static class GetTenantDomainsHandler
{
    public static async Task<Result<IReadOnlyList<TenantDomainResponse>>> Handle(
        GetTenantDomainsQuery query,
        ITenantDomainRepository domains,
        CancellationToken ct
    )
    {
        var tenantDomains = await domains.GetByTenantAsync(query.TenantId, ct);
        return Result.Success<IReadOnlyList<TenantDomainResponse>>(
            tenantDomains.Select(TenantDomainResponse.From).ToList()
        );
    }
}
