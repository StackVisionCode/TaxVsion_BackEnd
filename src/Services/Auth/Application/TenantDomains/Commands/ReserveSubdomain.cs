using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.TenantDomains.Commands;

/// <summary>
/// Fase A7 — bloquea un slug de subdominio para un email mientras el registro de la
/// oficina termina de completarse (ver Auth_y_CloudStorage_Plan_Completitud_v2.md §11,
/// pasos 2-3): el apex llama check-availability, y si el usuario decide seguir con ese
/// slug, llama a este comando antes de enviar el resto del formulario de alta. El
/// servicio Tenant nunca llama a Auth por HTTP para esto — la reserva es solo un gate
/// entre el frontend y Auth; el Tenant nace después, vía TenantCreatedIntegrationEvent,
/// que consume la reserva si sigue vigente (ver TenantCreatedConsumer).
/// </summary>
public sealed record ReserveSubdomainCommand(string? Slug, string? Email);

public sealed record SubdomainReservationResponse(string Slug, string ReservedByEmail, DateTime ExpiresAtUtc);

public static class ReserveSubdomainHandler
{
    public static async Task<Result<SubdomainReservationResponse>> Handle(
        ReserveSubdomainCommand command,
        ITenantDomainRepository domains,
        ITenantSubdomainReservationRepository reservations,
        IOptions<TenantDomainOptions> options,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var slugResult = SubdomainSlug.Create(command.Slug);
        if (slugResult.IsFailure)
            return Result.Failure<SubdomainReservationResponse>(slugResult.Error);

        var slug = slugResult.Value;
        var nowUtc = DateTime.UtcNow;

        if (await domains.SlugExistsAsync(slug.Value, ct))
            return Result.Failure<SubdomainReservationResponse>(
                new Error("TenantDomain.SlugTaken", "This subdomain is already in use.")
            );

        if (await reservations.GetActiveBySlugAsync(slug.Value, nowUtc, ct) is not null)
            return Result.Failure<SubdomainReservationResponse>(
                new Error("TenantDomain.SlugReservedTemporarily", "This subdomain is temporarily reserved.")
            );

        var reservationResult = TenantSubdomainReservation.Create(
            slug,
            command.Email ?? string.Empty,
            nowUtc,
            TimeSpan.FromMinutes(options.Value.SubdomainReservationTtlMinutes)
        );
        if (reservationResult.IsFailure)
            return Result.Failure<SubdomainReservationResponse>(reservationResult.Error);

        var reservation = reservationResult.Value;
        await reservations.AddAsync(reservation, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new SubdomainReservationResponse(
                reservation.SubdomainSlug,
                reservation.ReservedByEmail,
                reservation.ExpiresAtUtc
            )
        );
    }
}
