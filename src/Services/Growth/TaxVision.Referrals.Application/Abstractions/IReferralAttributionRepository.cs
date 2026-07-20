using TaxVision.Referrals.Domain.Attributions;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralAttributionRepository
{
    Task<ReferralAttribution?> GetByIdAsync(
        Guid attributionId,
        Guid ownerTenantId,
        CancellationToken ct = default
    );

    Task AddAsync(ReferralAttribution attribution, CancellationToken ct = default);
}
