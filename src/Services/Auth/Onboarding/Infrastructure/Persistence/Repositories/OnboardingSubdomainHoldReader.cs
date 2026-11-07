using TaxVision.Auth.Application.TenantDomains;
using TaxVision.Auth.Onboarding.Application.Abstractions;

namespace TaxVision.Auth.Onboarding.Infrastructure.Persistence.Repositories;

/// <summary>Le cuenta al núcleo si un slug está reservado por un onboarding en curso.</summary>
public sealed class OnboardingSubdomainHoldReader(IOnboardingSubdomainReservationRepository reservations)
    : ISubdomainHoldReader
{
    public async Task<bool> IsHeldAsync(string slug, DateTime nowUtc, CancellationToken ct = default) =>
        await reservations.GetActiveBySlugAsync(slug, nowUtc, ct) is not null;
}
