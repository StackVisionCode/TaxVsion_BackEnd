using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Abstractions;

/// <summary>
/// Repositorio del aggregate <see cref="TenantSignatureSettings"/>. Un registro por
/// tenant (unique index). Todas las consultas filtran implícitamente por TenantId al
/// venir por Id o por TenantId directo.
/// </summary>
public interface ITenantSignatureSettingsRepository
{
    Task<TenantSignatureSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(TenantSignatureSettings settings, CancellationToken ct = default);
}
