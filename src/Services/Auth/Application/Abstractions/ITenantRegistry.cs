namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantRegistry
{
    Task<bool> ExistsActiveAsync(Guid tenantId, CancellationToken ct = default);
    Task<Domain.Tenants.Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default);
    Task UpsertCreatedAsync(
        Guid tenantId,
        string name,
        string subDomain,
        string adminEmail,
        string adminInvitationTokenHash,
        CancellationToken ct = default);
    Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default);
}
