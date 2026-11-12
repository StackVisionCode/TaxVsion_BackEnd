using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralAttributionRepository
{
    Task<ReferralAttribution?> GetByIdAsync(Guid attributionId, Guid ownerTenantId, CancellationToken ct = default);

    /// <summary>
    /// Atribución de un referido concreto (el alta aún sin oficina). Acotada al tenant dueño, que debe coincidir con el
    /// activo; devuelve null si ese referido no llegó con un código.
    /// </summary>
    Task<ReferralAttribution?> GetByRefereeAsync(Guid ownerTenantId, Guid refereeId, CancellationToken ct = default);

    Task AddAsync(ReferralAttribution attribution, CancellationToken ct = default);
}
