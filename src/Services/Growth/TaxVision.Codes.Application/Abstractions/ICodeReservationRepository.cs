using TaxVision.Codes.Domain.Reservations;

namespace TaxVision.Codes.Application.Abstractions;

public interface ICodeReservationRepository
{
    Task<CodeReservation?> GetByIdAsync(Guid tenantId, Guid reservationId, CancellationToken ct = default);

    Task AddAsync(CodeReservation reservation, CancellationToken ct = default);

    /// <summary>
    /// Barrido de sistema (cross-tenant): reservas <c>Active</c> cuyo <c>ExpiresAtUtc</c> ya pasó.
    /// Alimenta al <c>ReservationExpirySweeper</c> — la red de seguridad que libera el código de un
    /// checkout abandonado (nadie llamó Cancel). No usa el guard de tenant porque es un scan del sistema.
    /// </summary>
    Task<IReadOnlyList<ExpiredReservationRef>> GetActiveExpiredAsync(
        DateTime nowUtc,
        int batchSize,
        CancellationToken ct = default
    );
}

/// <summary>Referencia mínima (tenant + reserva) para expirar una reserva vencida con su tenant correcto.</summary>
public sealed record ExpiredReservationRef(Guid TenantId, Guid ReservationId);
