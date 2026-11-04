using TaxVision.Tenant.Application.Compliance.Abstractions;

namespace TaxVision.Tenant.Application.Compliance.Queries;

/// <summary>Pre-flight del retiro: ¿el empleado es el responsable de seguridad de la oficina?</summary>
public sealed record GetTenantOffboardingImpactQuery(Guid TenantId, Guid UserId);

/// <summary>Si es <c>true</c>, al retirarlo la designación queda vacante y se avisa a los admins.</summary>
public sealed record TenantOffboardingImpactResponse(bool IsSecurityCoordinator);

public static class GetTenantOffboardingImpactHandler
{
    public static async Task<TenantOffboardingImpactResponse> Handle(
        GetTenantOffboardingImpactQuery query,
        IOfficeComplianceRepository compliances,
        CancellationToken ct
    )
    {
        var compliance = await compliances.GetAsync(query.TenantId, ct);
        return new TenantOffboardingImpactResponse(
            query.UserId != Guid.Empty && compliance?.SecurityCoordinatorUserId == query.UserId
        );
    }
}
