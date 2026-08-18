using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;

namespace TaxVision.Auth.Application.Onboarding.Abstractions;

public interface IOnboardingSubdomainReservationRepository
{
    /// <summary>La reserva activa (no consumida, no expirada) para este slug — de cualquier
    /// onboarding. Null si el slug está libre.</summary>
    Task<OnboardingSubdomainReservation?> GetActiveBySlugAsync(
        string slug,
        DateTime nowUtc,
        CancellationToken ct = default
    );

    Task AddAsync(OnboardingSubdomainReservation reservation, CancellationToken ct = default);
}
