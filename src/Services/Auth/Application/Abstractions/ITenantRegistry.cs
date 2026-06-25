namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantRegistry
{
    Task<bool> ExistsActiveAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertCreatedAsync(Guid tenantId, string name, string subDomain, CancellationToken ct = default);
}
