using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Application.Tenants.Commands;

namespace TaxVision.Tenant.Application.Tenants.Queries;

/// <summary>La oficina del que llama (tenant del JWT): nombre, subdominio y zona horaria.</summary>
public sealed record GetCurrentTenantQuery(Guid TenantId);

public static class GetCurrentTenantHandler
{
    public static async Task<Result<TenantResponse>> Handle(
        GetCurrentTenantQuery query,
        ITenantRepository tenants,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, ct);
        return tenant is null
            ? Result.Failure<TenantResponse>(new Error("Tenant.NotFound", "Tenant does not exist."))
            : Result.Success(new TenantResponse(tenant.Id, tenant.Name, tenant.SubDomain, tenant.DefaultTimeZoneId));
    }
}
