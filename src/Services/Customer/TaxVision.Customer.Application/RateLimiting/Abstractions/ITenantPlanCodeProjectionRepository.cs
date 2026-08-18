using TaxVision.Customer.Domain.RateLimiting;

namespace TaxVision.Customer.Application.RateLimiting.Abstractions;

/// <summary>
/// RateLimit Fase 1 — puerto no-genérico de Customer sobre el repo genérico de BuildingBlocks;
/// la invalidación de caché (<c>BuildingBlocks.RateLimiting.ITenantPlanCodeCacheInvalidator</c>)
/// ya se referencia directo desde el consumer, sin un puerto local duplicado.
/// </summary>
public interface ITenantPlanCodeProjectionRepository
    : BuildingBlocks.RateLimiting.ITenantPlanCodeProjectionRepository<TenantPlanCodeProjection> { }
