using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.Onboarding.SubdomainReservations.Commands;

public sealed record ReserveSubdomainForOnboardingCommand(string? Slug, Guid OnboardingId, string Email);

public sealed record SubdomainReservationResponse(bool Available, string? Reason, DateTime? ExpiresAtUtc);

/// <summary>
/// PayFlow (Fase 14) — único endpoint del plan (<c>POST onboarding/subdomains/check</c>) hace
/// check-y-reserva en un solo paso: si el slug está libre, lo reserva por
/// <see cref="OnboardingOptions.SubdomainReservationTtlMinutes"/> para este onboarding. Repite los
/// mismos chequeos que <see cref="Queries.CheckSubdomainAvailabilityHandler"/> (no lo invoca vía bus
/// — mismo criterio que <c>ReserveSubdomainHandler</c> de TenantDomains, que tampoco compone
/// handlers entre sí) para poder actuar sobre el resultado en la misma transacción.
/// </summary>
public static class ReserveSubdomainForOnboardingHandler
{
    public static async Task<Result<SubdomainReservationResponse>> Handle(
        ReserveSubdomainForOnboardingCommand command,
        IOnboardingSubdomainReservationRepository reservations,
        ITenantSubdomainAvailabilityClient tenantAvailability,
        IOptions<OnboardingOptions> onboardingOptions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (command.OnboardingId == Guid.Empty)
            return Result.Failure<SubdomainReservationResponse>(
                new Error("Onboarding.SubdomainReservationOnboardingId", "An onboarding id is required.")
            );

        var slugResult = SubdomainSlug.Create(command.Slug);
        if (slugResult.IsFailure)
            return Result.Success(new SubdomainReservationResponse(false, slugResult.Error.Code, null));

        var slug = slugResult.Value;
        var nowUtc = DateTime.UtcNow;
        var ttl = TimeSpan.FromMinutes(onboardingOptions.Value.SubdomainReservationTtlMinutes);

        var activeReservation = await reservations.GetActiveBySlugAsync(slug.Value, nowUtc, ct);
        if (activeReservation is not null && activeReservation.OnboardingId != command.OnboardingId)
            return Result.Success(
                new SubdomainReservationResponse(false, "Onboarding.SubdomainReservedTemporarily", null)
            );

        var takenResult = await tenantAvailability.IsTakenAsync(slug.Value, ct);
        if (takenResult.IsFailure)
            return Result.Failure<SubdomainReservationResponse>(takenResult.Error);

        if (takenResult.Value)
            return Result.Success(new SubdomainReservationResponse(false, "Onboarding.SubdomainTaken", null));

        if (activeReservation is not null)
        {
            // Idempotente: el mismo onboarding re-chequeando el mismo slug solo extiende el TTL.
            var renewResult = activeReservation.Renew(nowUtc, ttl);
            if (renewResult.IsFailure)
                return Result.Failure<SubdomainReservationResponse>(renewResult.Error);

            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(new SubdomainReservationResponse(true, null, activeReservation.ExpiresAtUtc));
        }

        var reservationResult = OnboardingSubdomainReservation.Create(
            slug,
            command.OnboardingId,
            command.Email,
            nowUtc,
            ttl
        );
        if (reservationResult.IsFailure)
            return Result.Failure<SubdomainReservationResponse>(reservationResult.Error);

        var reservation = reservationResult.Value;
        await reservations.AddAsync(reservation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new SubdomainReservationResponse(true, null, reservation.ExpiresAtUtc));
    }
}
