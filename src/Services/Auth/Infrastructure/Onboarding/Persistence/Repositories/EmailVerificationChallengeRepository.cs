using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.EmailVerification;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;

public sealed class EmailVerificationChallengeRepository(AuthDbContext db) : IEmailVerificationChallengeRepository
{
    public Task<EmailVerificationChallenge?> GetByIdAsync(Guid challengeId, CancellationToken ct = default) =>
        db.EmailVerificationChallenges.FirstOrDefaultAsync(challenge => challenge.Id == challengeId, ct);

    public async Task AddAsync(EmailVerificationChallenge challenge, CancellationToken ct = default) =>
        await db.EmailVerificationChallenges.AddAsync(challenge, ct);
}
