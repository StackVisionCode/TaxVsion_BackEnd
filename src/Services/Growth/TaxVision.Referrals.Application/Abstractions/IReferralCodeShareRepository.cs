using TaxVision.Referrals.Domain.Shares;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralCodeShareRepository
{
    Task AddAsync(ReferralCodeShare share, CancellationToken ct = default);
}
