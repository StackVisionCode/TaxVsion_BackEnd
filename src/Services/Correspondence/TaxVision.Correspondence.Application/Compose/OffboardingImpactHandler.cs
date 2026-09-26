using TaxVision.Correspondence.Application.Abstractions;

namespace TaxVision.Correspondence.Application.Compose;

// Pre-flight de impacto (punto 3.2): cuántos borradores abiertos tiene este empleado (para reasignarlos
// al sucesor o descartarlos al retirarlo).
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId);

public sealed record OffboardingImpactResponse(int OpenDrafts);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        IDraftRepository drafts,
        CancellationToken ct
    )
    {
        var openDrafts = await drafts.CountOpenByAuthorAsync(query.TenantId, query.UserId, ct);
        return new OffboardingImpactResponse(openDrafts);
    }
}
