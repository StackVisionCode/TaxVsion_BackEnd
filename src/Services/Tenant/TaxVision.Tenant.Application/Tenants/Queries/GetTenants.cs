using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;
using BuildingBlocks.Caching;
using TaxVision.Tenant.Application.Tenants;
using Microsoft.Extensions.Logging;

namespace TaxVision.Tenant.Application.Tenants.Queries;

public sealed record GetTenantsQuery(int Page = 1, int Size = 20);
public static class GetTenantsHandler
{
    public static async Task<IReadOnlyList<TenantResponse>> Handle(
    GetTenantsQuery q,
    ITenantReadService reader,
    ICacheService cache,
    ILogger<GetTenantsQuery> logger,
    CancellationToken ct)
    {
        try
        {
            return await TenantListCache.GetPageAsync(
                cache,
                q.Page,
                q.Size,
                token => reader.GetPageAsync(q.Page, q.Size, token),
                ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tenant list cache is unavailable; using SQL Server.");
            return await reader.GetPageAsync(q.Page, q.Size, ct);
        }
    }
}
