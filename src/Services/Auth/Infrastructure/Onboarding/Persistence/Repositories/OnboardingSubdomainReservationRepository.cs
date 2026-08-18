using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Infrastructure.Persistence;

namespace TaxVision.Auth.Infrastructure.Onboarding.Persistence.Repositories;

public sealed class OnboardingSubdomainReservationRepository(AuthDbContext db)
    : IOnboardingSubdomainReservationRepository
{
    public Task<OnboardingSubdomainReservation?> GetActiveBySlugAsync(
        string slug,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        db.OnboardingSubdomainReservations.FirstOrDefaultAsync(
            r => r.Slug == slug && r.ConsumedAtUtc == null && r.ExpiresAtUtc > nowUtc,
            ct
        );

    public async Task AddAsync(OnboardingSubdomainReservation reservation, CancellationToken ct = default) =>
        await db.OnboardingSubdomainReservations.AddAsync(reservation, ct);
}
