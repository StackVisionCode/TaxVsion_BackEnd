namespace TaxVision.Mobile.Application.RateLimiting.Abstractions;

public sealed record TenantEntitlementsSnapshot(
    string PlanCode,
    long RevisionNumber,
    IReadOnlyList<string> EnabledModules
);

/// <summary>Trae de Subscription el plan vigente del tenant para reparar la proyección. Null si no puede.</summary>
public interface ITenantEntitlementsSnapshotClient
{
    Task<TenantEntitlementsSnapshot?> FetchAsync(Guid tenantId, CancellationToken ct = default);
}
