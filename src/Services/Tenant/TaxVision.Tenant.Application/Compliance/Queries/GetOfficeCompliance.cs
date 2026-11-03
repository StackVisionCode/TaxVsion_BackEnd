using TaxVision.Tenant.Application.Compliance.Abstractions;

namespace TaxVision.Tenant.Application.Compliance.Queries;

/// <summary>Cumplimiento de la oficina del que llama (solo administradores).</summary>
public sealed record GetOfficeComplianceQuery(Guid TenantId);

public static class GetOfficeComplianceHandler
{
    public static async Task<OfficeComplianceResponse> Handle(
        GetOfficeComplianceQuery query,
        IOfficeComplianceRepository compliances,
        CancellationToken ct
    ) => OfficeComplianceResponse.From(await compliances.GetAsync(query.TenantId, ct));
}
