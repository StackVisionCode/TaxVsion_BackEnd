namespace TaxVision.Tenant.Application.Tenants.Abstractions;

public interface ITenantRepository
{
    Task<TaxVision.Tenant.Domain.Tenant?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TaxVision.Tenant.Domain.Tenant entity, CancellationToken ct = default);
    void Remove(TaxVision.Tenant.Domain.Tenant entity);

    Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 16) — idempotencia de internal/tenants/from-onboarding.</summary>
    Task<TaxVision.Tenant.Domain.Tenant?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);
}
