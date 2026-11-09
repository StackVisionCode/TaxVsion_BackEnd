using TaxVision.Auth.Onboarding.Domain.SubdomainReservations;

namespace TaxVision.Auth.Onboarding.Application.Abstractions;

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
