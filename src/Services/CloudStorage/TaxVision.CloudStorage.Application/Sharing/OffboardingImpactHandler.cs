using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Sharing;

// Pre-flight de impacto (punto 3.2): share links activos que se revocan y conexiones de nube de la oficina
// ligadas a la persona (se marcan para revisión, no se revocan). El email es opcional: sin él solo cuenta
// las que autorizó (CloudStorage no tiene directorio de usuarios).
public sealed record OffboardingImpactQuery(Guid TenantId, Guid UserId, string? Email = null);

public sealed record OffboardingImpactResponse(int ActiveShareLinks, int OfficeCloudConnections);

public static class OffboardingImpactHandler
{
    public static async Task<OffboardingImpactResponse> Handle(
        OffboardingImpactQuery query,
        IShareLinkRepository shareLinks,
        ITenantCloudConnectionRepository cloudConnections,
        CancellationToken ct
    )
    {
        var active = await shareLinks.CountActiveByCreatorAsync(query.TenantId, query.UserId, ct);
        var connections = await cloudConnections.ListByTenantAsync(query.TenantId, ct);
        var linked = connections.Count(c => c.LinkedReasonFor(query.UserId, query.Email) is not null);
        return new OffboardingImpactResponse(active, linked);
    }
}
