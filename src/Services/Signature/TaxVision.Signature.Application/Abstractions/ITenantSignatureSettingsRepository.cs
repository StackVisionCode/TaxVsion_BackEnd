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

    /// <summary>Tenants cuya oficina activó la purga (el plan se revisa aparte, con los techos vigentes).</summary>
    Task<IReadOnlyList<Guid>> ListTenantIdsWithPurgeEnabledAsync(CancellationToken ct = default);
}
