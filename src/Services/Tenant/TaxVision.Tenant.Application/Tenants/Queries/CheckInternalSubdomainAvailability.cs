using TaxVision.Tenant.Application.Tenants.Abstractions;

namespace TaxVision.Tenant.Application.Tenants.Queries;

public sealed record CheckInternalSubdomainAvailabilityQuery(string Slug);

public sealed record SubdomainAvailabilityResponse(bool Taken);

/// <summary>PayFlow (Fase 14) — invocado M2M por Auth (TenantSubdomainAvailabilityClient) durante
/// el registro post-pago, antes de que el tenant exista.</summary>
public static class CheckInternalSubdomainAvailabilityHandler
{
    public static async Task<SubdomainAvailabilityResponse> Handle(
        CheckInternalSubdomainAvailabilityQuery query,
        ITenantRepository repo,
        CancellationToken ct
    )
    {
        var taken = await repo.SubDomainExistsAsync(query.Slug, ct);
        return new SubdomainAvailabilityResponse(taken);
    }
}
