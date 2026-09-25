using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Sharing;

// Pre-flight de impacto (punto 3.2): cuántos share links activos creó este empleado (se revocan al retirarlo).
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int ActiveShareLinks);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        IShareLinkRepository shareLinks,
        CancellationToken ct
    )
    {
        var active = await shareLinks.CountActiveByCreatorAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(active);
    }
}
