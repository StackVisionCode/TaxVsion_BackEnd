using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.Onboarding.SubdomainReservations.Queries;

/// <summary>PayFlow (Fase 14) — mismo criterio que su equivalente en TenantDomains
/// (CheckSubdomainAvailabilityHandler) para los resultados locales: formato inválido / reservado
/// por otro onboarding / ya tomado en Tenant son resultados válidos, no errores. Difiere en un
/// punto: si el M2M a Tenant falla (servicio caído), esto SÍ propaga un Result de error — a
/// diferencia de un slug simplemente inválido, no hay forma honesta de responder
/// "disponible"/"no disponible" sin saber si Tenant ya lo tiene, y silenciarlo arriesgaría un falso
/// "available" que colisione más tarde en Fase 16.</summary>
public sealed record SubdomainAvailabilityResponse(bool Available, string? Reason);

public sealed record CheckSubdomainAvailabilityQuery(string? Slug, Guid? OnboardingId);

public static class CheckSubdomainAvailabilityHandler
{
    public static async Task<Result<SubdomainAvailabilityResponse>> Handle(
        CheckSubdomainAvailabilityQuery query,
        IOnboardingSubdomainReservationRepository reservations,
        ITenantSubdomainAvailabilityClient tenantAvailability,
        CancellationToken ct
    )
    {
        var slugResult = SubdomainSlug.Create(query.Slug);
        if (slugResult.IsFailure)
            return Result.Success(new SubdomainAvailabilityResponse(false, slugResult.Error.Code));

        var slug = slugResult.Value.Value;
        var nowUtc = DateTime.UtcNow;

        var activeReservation = await reservations.GetActiveBySlugAsync(slug, nowUtc, ct);
        if (activeReservation is not null && activeReservation.OnboardingId != query.OnboardingId)
            return Result.Success(new SubdomainAvailabilityResponse(false, "Onboarding.SubdomainReservedTemporarily"));

        var takenResult = await tenantAvailability.IsTakenAsync(slug, ct);
        if (takenResult.IsFailure)
            return Result.Failure<SubdomainAvailabilityResponse>(takenResult.Error);

        if (takenResult.Value)
            return Result.Success(new SubdomainAvailabilityResponse(false, "Onboarding.SubdomainTaken"));

        return Result.Success(new SubdomainAvailabilityResponse(true, null));
    }
}
