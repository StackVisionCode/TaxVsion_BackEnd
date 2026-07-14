using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantTermsAcceptanceRepository
{
    Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default);

    /// <summary>Ultima aceptacion registrada del tenant (cualquier version) — null si nunca acepto nada.</summary>
    Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default);
}
