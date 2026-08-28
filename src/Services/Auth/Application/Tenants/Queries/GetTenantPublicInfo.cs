using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.Queries;

/// <summary>
/// Fase A4 — datos públicos mínimos de un tenant ya resuelto por Host. El branding NO vive en Auth:
/// el front lo pide al servicio Tenant (endpoint público de TenantBrands), que es su owner (DDD).
/// </summary>
public sealed record TenantResolutionResponse(Guid TenantId, string Name, string Status);

public sealed record GetTenantPublicInfoQuery(Guid TenantId);

public static class GetTenantPublicInfoHandler
{
    public static async Task<Result<TenantResolutionResponse>> Handle(
        GetTenantPublicInfoQuery query,
        ITenantRegistry tenants,
        CancellationToken ct
    )
    {
        var tenant = await tenants.GetByIdAsync(query.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<TenantResolutionResponse>(new Error("Tenant.NotFound", "Tenant not found."));

        return Result.Success(new TenantResolutionResponse(tenant.Id, tenant.Name, "Active"));
    }
}
