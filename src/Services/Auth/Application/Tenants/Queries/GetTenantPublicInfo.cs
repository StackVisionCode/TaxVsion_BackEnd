using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Tenants.Queries;

/// <summary>
/// Fase A4 — datos públicos mínimos de un tenant ya resuelto por Host. LogoUrl
/// queda en null: hoy ni el Tenant projection ni TenantCreatedIntegrationEvent
/// transportan branding — pendiente de un campo dedicado cuando exista.
/// </summary>
public sealed record TenantResolutionResponse(Guid TenantId, string Name, string? LogoUrl, string Status);

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

        return Result.Success(new TenantResolutionResponse(tenant.Id, tenant.Name, LogoUrl: null, "Active"));
    }
}
