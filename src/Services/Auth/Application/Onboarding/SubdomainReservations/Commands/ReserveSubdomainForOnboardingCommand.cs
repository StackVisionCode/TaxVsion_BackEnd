using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Domain.Onboarding.SubdomainReservations;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Application.Onboarding.SubdomainReservations.Commands;

public sealed record ReserveSubdomainForOnboardingCommand(string? Slug, string Token);

public sealed record SubdomainReservationResponse(bool Available, string? Reason, DateTime? ExpiresAtUtc);

/// <summary>
/// PayFlow (Fase 14) — único endpoint del plan (<c>POST onboarding/subdomains/check</c>) hace
/// check-y-reserva en un solo paso: si el slug está libre, lo reserva por
/// <see cref="OnboardingOptions.SubdomainReservationTtlMinutes"/> para este onboarding. Repite los
/// mismos chequeos que <see cref="Queries.CheckSubdomainAvailabilityHandler"/> (no lo invoca vía bus
/// — mismo criterio que <c>ReserveSubdomainHandler</c> de TenantDomains, que tampoco compone
/// handlers entre sí) para poder actuar sobre el resultado en la misma transacción.
/// <para>
/// Auditoría F11 — además de su propia tabla, ahora también consulta
/// <see cref="ITenantSubdomainReservationRepository"/> (Path A / <c>TenantDomains</c>): sin este
/// chequeo, dos compradores concurrentes podían reservar el mismo slug por caminos distintos (uno
/// vía onboarding pago-primero, el otro vía el flujo de alta directa) — ninguno de los dos lados se
/// enteraba de la reserva del otro hasta que <c>ITenantSubdomainAvailabilityClient</c> (que solo ve
/// tenants ya creados, no reservas en vuelo) lo detectaba, si es que llegaba a detectarlo a tiempo.
/// </para>
/// <para>
/// Auditoría frontend (post-Fase 14) — el comando pasó de recibir <c>OnboardingId</c>/<c>Email</c>
/// crudos del cliente a recibir el mismo <c>Token</c> opaco que ya usan
/// <see cref="Registration.Commands.CompleteOnboardingRegistrationCommand"/> y
/// <see cref="Registration.Queries.PreviewRegistrationQuery"/>, resolviendo el onboarding
/// server-side por el hash del token (mismo patrón: <c>Onboarding.InvalidToken</c>/<c>TokenUsed</c>/
/// <c>TokenExpired</c>). Era el único endpoint del módulo que exigía el <c>OnboardingId</c> real al
/// cliente — un valor que el invariante de diseño (§5 del API_Contract: el id se expone una única
/// vez, en la respuesta de <c>POST onboarding</c>, y solo vive en memoria de esa sesión de compra)
/// nunca le entrega. El link de registro llega por email después de un webhook asíncrono: si el
/// comprador lo abre en otra pestaña/dispositivo/día, el <c>OnboardingId</c> ya no existe en el
/// cliente y el endpoint viejo era permanentemente inalcanzable. El <c>Email</c> crudo del request
/// también se reemplaza por <c>onboarding.Email</c> ya validado — evita que el cliente mande un
/// email arbitrario para la reserva.
/// </para>
/// </summary>
public static class ReserveSubdomainForOnboardingHandler
{
    public static async Task<Result<SubdomainReservationResponse>> Handle(
        ReserveSubdomainForOnboardingCommand command,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        IOnboardingSubdomainReservationRepository reservations,
        ITenantSubdomainReservationRepository tenantDomainReservations,
        ITenantSubdomainAvailabilityClient tenantAvailability,
        IOptions<OnboardingOptions> onboardingOptions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var onboardingResult = await ResolveOnboardingByTokenAsync(command.Token, onboardings, tokens, ct);
        if (onboardingResult.IsFailure)
            return Result.Failure<SubdomainReservationResponse>(onboardingResult.Error);

        var onboarding = onboardingResult.Value;

        var slugResult = SubdomainSlug.Create(command.Slug);
        if (slugResult.IsFailure)
            return Result.Success(new SubdomainReservationResponse(false, slugResult.Error.Code, null));

        var slug = slugResult.Value;
        var nowUtc = DateTime.UtcNow;
        var ttl = TimeSpan.FromMinutes(onboardingOptions.Value.SubdomainReservationTtlMinutes);

        var activeReservation = await reservations.GetActiveBySlugAsync(slug.Value, nowUtc, ct);
        if (activeReservation is not null && activeReservation.OnboardingId != onboarding.Id)
            return Result.Success(
                new SubdomainReservationResponse(false, "Onboarding.SubdomainReservedTemporarily", null)
            );

        if (await tenantDomainReservations.GetActiveBySlugAsync(slug.Value, nowUtc, ct) is not null)
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
            onboarding.Id,
            onboarding.Email,
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

    /// <summary>Mismas 3 validaciones (token inválido/usado/vencido) y mismo orden que
    /// <c>PreviewRegistrationHandler</c>/<c>CompleteOnboardingRegistrationHandler.LoadRegistrationContextAsync</c>
    /// — un token que ya no sirve para completar el registro tampoco debería poder reservar un
    /// subdominio.</summary>
    private static async Task<
        Result<Domain.Onboarding.TenantOnboardings.TenantOnboarding>
    > ResolveOnboardingByTokenAsync(
        string? token,
        ITenantOnboardingRepository onboardings,
        ISecureTokenService tokens,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(token))
            return Result.Failure<Domain.Onboarding.TenantOnboardings.TenantOnboarding>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        var hash = tokens.Hash(token).ToLowerInvariant();
        var onboarding = await onboardings.GetByRegistrationTokenHashAsync(hash, ct);
        if (onboarding is null)
            return Result.Failure<Domain.Onboarding.TenantOnboardings.TenantOnboarding>(
                new Error("Onboarding.InvalidToken", "The registration token is invalid.")
            );

        if (onboarding.RegistrationTokenUsedAtUtc is not null)
            return Result.Failure<Domain.Onboarding.TenantOnboardings.TenantOnboarding>(
                new Error("Onboarding.TokenUsed", "The registration token was already used.")
            );

        if (
            onboarding.RegistrationTokenExpiresAtUtc is null
            || DateTime.UtcNow >= onboarding.RegistrationTokenExpiresAtUtc
        )
            return Result.Failure<Domain.Onboarding.TenantOnboardings.TenantOnboarding>(
                new Error("Onboarding.TokenExpired", "The registration token has expired.")
            );

        return Result.Success(onboarding);
    }
}
