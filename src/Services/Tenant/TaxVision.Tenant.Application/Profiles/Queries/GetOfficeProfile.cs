using TaxVision.Tenant.Application.Profiles.Abstractions;

namespace TaxVision.Tenant.Application.Profiles.Queries;

/// <summary>Perfil público de la oficina del que llama (tenant del JWT).</summary>
public sealed record GetOfficeProfileQuery(Guid TenantId);

public static class GetOfficeProfileHandler
{
    public static async Task<OfficeProfileResponse> Handle(
        GetOfficeProfileQuery query,
        IOfficeProfileRepository profiles,
        CancellationToken ct
    ) => OfficeProfileResponses.From(await profiles.GetAsync(query.TenantId, ct));
}
