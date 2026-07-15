using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Auth.Domain.TenantDomains;

/// <summary>
/// Reserva temporal de un slug de subdominio mientras se completa el alta de una oficina
/// (registro -&gt; disponibilidad -&gt; reserva -&gt; creación del tenant -&gt; TenantDomain). Evita
/// que dos registros concurrentes se queden con el mismo slug entre el chequeo de
/// disponibilidad y la creación real del tenant. TTL corto; un job de limpieza (Fase A5/A6)
/// purga las reservas expiradas y no consumidas.
/// </summary>
public sealed class TenantSubdomainReservation : BaseEntity
{
    private TenantSubdomainReservation() { }

    public string SubdomainSlug { get; private set; } = default!;
    public string ReservedByEmail { get; private set; } = default!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<TenantSubdomainReservation> Create(
        SubdomainSlug slug,
        string reservedByEmail,
        DateTime nowUtc,
        TimeSpan ttl
    )
    {
        var normalizedEmail = reservedByEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (normalizedEmail.Length == 0 || !normalizedEmail.Contains('@'))
            return Result.Failure<TenantSubdomainReservation>(
                new Error("TenantDomain.ReservationEmail", "A valid email is required to reserve a subdomain.")
            );

        if (ttl <= TimeSpan.Zero)
            return Result.Failure<TenantSubdomainReservation>(
                new Error("TenantDomain.ReservationTtl", "Reservation TTL must be positive.")
            );

        return Result.Success(
            new TenantSubdomainReservation
            {
                Id = Guid.NewGuid(),
                SubdomainSlug = slug.Value,
                ReservedByEmail = normalizedEmail,
                CreatedAtUtc = nowUtc,
                ExpiresAtUtc = nowUtc.Add(ttl),
            }
        );
    }

    public bool IsExpired(DateTime nowUtc) => ConsumedAtUtc is null && nowUtc >= ExpiresAtUtc;

    public bool IsActive(DateTime nowUtc) => ConsumedAtUtc is null && nowUtc < ExpiresAtUtc;

    public Result Consume(DateTime nowUtc)
    {
        if (ConsumedAtUtc is not null)
            return Result.Failure(new Error("TenantDomain.ReservationConsumed", "Reservation was already consumed."));

        if (IsExpired(nowUtc))
            return Result.Failure(new Error("TenantDomain.ReservationExpired", "Reservation has expired."));

        ConsumedAtUtc = nowUtc;
        return Result.Success();
    }
}
