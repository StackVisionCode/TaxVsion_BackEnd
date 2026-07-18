using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Queries;

/// <summary>
/// Fase A4 — usado por el sitio de alta/registro (apex) y por el servicio Tenant antes
/// de crear el tenant (ver Auth_y_CloudStorage_Plan_Completitud_v2.md §11.1). Nunca
/// falla: formato inválido / reservado / ya tomado son resultados válidos, no errores.
/// </summary>
public sealed record SubdomainAvailabilityResponse(bool Available, string? Reason);

public sealed record CheckSubdomainAvailabilityQuery(string? Slug);

public static class CheckSubdomainAvailabilityHandler
{
    public static async Task<Result<SubdomainAvailabilityResponse>> Handle(
        CheckSubdomainAvailabilityQuery query,
        ITenantDomainRepository domains,
        ITenantSubdomainReservationRepository reservations,
        CancellationToken ct
    )
    {
        var slugResult = SubdomainSlug.Create(query.Slug);
        if (slugResult.IsFailure)
            return Result.Success(new SubdomainAvailabilityResponse(false, slugResult.Error.Code));

        var slug = slugResult.Value.Value;

        if (await domains.SlugExistsAsync(slug, ct))
            return Result.Success(new SubdomainAvailabilityResponse(false, "TenantDomain.SlugTaken"));

        if (await reservations.GetActiveBySlugAsync(slug, DateTime.UtcNow, ct) is not null)
            return Result.Success(new SubdomainAvailabilityResponse(false, "TenantDomain.SlugReservedTemporarily"));

        return Result.Success(new SubdomainAvailabilityResponse(true, null));
    }
}
