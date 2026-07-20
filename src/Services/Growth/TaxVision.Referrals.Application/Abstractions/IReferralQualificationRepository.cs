using TaxVision.Referrals.Domain.Qualifications;

namespace TaxVision.Referrals.Application.Abstractions;

public interface IReferralQualificationRepository
{
    Task AddAsync(ReferralQualification qualification, CancellationToken ct = default);
}
