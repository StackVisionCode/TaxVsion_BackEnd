using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Domain.Onboarding.SubdomainReservations;

/// <summary>
/// PayFlow (Fase 14) — reserva temporal (TTL 60min) de un slug de subdominio elegido durante el
/// registro post-pago, para que dos compradores concurrentes no se queden con el mismo slug entre
/// el chequeo de disponibilidad y <c>CompleteOnboardingRegistrationHandler.StartProvisioning</c>.
/// Aggregate separado (no reusa <see cref="TenantDomains.TenantSubdomainReservation"/>, prohibido
/// para el módulo Onboarding por OnboardingModuleArchitectureTests) pero mismo shape/semántica —
/// mismo patrón "reserva única activa por slug, TTL corto, consumida o expira" ya usado ahí.
/// </summary>
public sealed class OnboardingSubdomainReservation : BaseEntity
{
    private OnboardingSubdomainReservation() { }

    public string Slug { get; private set; } = default!;
    public Guid OnboardingId { get; private set; }
    public string ReservedByEmail { get; private set; } = default!;
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public static Result<OnboardingSubdomainReservation> Create(
        SubdomainSlug slug,
        Guid onboardingId,
        string reservedByEmail,
        DateTime nowUtc,
        TimeSpan ttl
    )
    {
        if (onboardingId == Guid.Empty)
            return Result.Failure<OnboardingSubdomainReservation>(
                new Error("Onboarding.SubdomainReservationOnboardingId", "An onboarding id is required.")
            );

        var normalizedEmail = reservedByEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
            return Result.Failure<OnboardingSubdomainReservation>(
                new Error("Onboarding.SubdomainReservationEmail", "A valid email is required to reserve a subdomain.")
            );

        if (ttl <= TimeSpan.Zero)
            return Result.Failure<OnboardingSubdomainReservation>(
                new Error("Onboarding.SubdomainReservationTtl", "Reservation TTL must be positive.")
            );

        return Result.Success(
            new OnboardingSubdomainReservation
            {
                Id = Guid.NewGuid(),
                Slug = slug.Value,
                OnboardingId = onboardingId,
                ReservedByEmail = normalizedEmail,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(ttl),
            }
        );
    }

    public bool IsExpired(DateTime nowUtc) => ConsumedAtUtc is null && nowUtc >= ExpiresAtUtc;

    public bool IsActive(DateTime nowUtc) => ConsumedAtUtc is null && nowUtc < ExpiresAtUtc;

    /// <summary>Extiende una reserva ya activa del mismo onboarding (idempotente: el frontend puede
    /// re-chequear el mismo slug varias veces mientras completa el form).</summary>
    public Result Renew(DateTime nowUtc, TimeSpan ttl)
    {
        if (ttl <= TimeSpan.Zero)
            return Result.Failure(new Error("Onboarding.SubdomainReservationTtl", "Reservation TTL must be positive."));

        ExpiresAtUtc = nowUtc.Add(ttl);
        return Result.Success();
    }

    public Result Consume(DateTime nowUtc)
    {
        if (ConsumedAtUtc is not null)
            return Result.Failure(
                new Error("Onboarding.SubdomainReservationConsumed", "Reservation was already consumed.")
            );

        if (IsExpired(nowUtc))
            return Result.Failure(new Error("Onboarding.SubdomainReservationExpired", "Reservation has expired."));

        ConsumedAtUtc = nowUtc;
        return Result.Success();
    }
}
