using BuildingBlocks.Persistence;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

public interface ITenantRepository : IRepository<TaxVision.Tenant.Domain.Tenant>
{
    Task<bool> SubDomainExistsAsync(string subdomain, CancellationToken ct = default);

    /// <summary>PayFlow (Fase 16) — idempotencia de tenants/internal/from-onboarding.</summary>
    Task<TaxVision.Tenant.Domain.Tenant?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default);
}
