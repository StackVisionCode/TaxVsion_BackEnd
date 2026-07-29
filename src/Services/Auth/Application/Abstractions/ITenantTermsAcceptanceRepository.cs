using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Application.Abstractions;

public interface ITenantTermsAcceptanceRepository
{
    Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default);

    /// <summary>Ultima aceptacion registrada del tenant (cualquier version) — null si nunca acepto nada.</summary>
    Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default);

    /// <summary>Aceptacion existente de un usuario para una TermsVersion puntual — soporte del check-then-insert idempotente (PayFlow Fase 6).</summary>
    Task<TenantTermsAcceptance?> GetByVersionAsync(
        Guid tenantId,
        Guid userId,
        Guid termsVersionId,
        CancellationToken ct = default
    );
}
