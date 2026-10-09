using TaxVision.Wallet.Domain.Reservations;

namespace TaxVision.Wallet.Application.Reservations.Abstractions;

/// <summary>
/// Repositorio de <see cref="WalletReservation"/> (PEP money-OUT). Único por
/// <c>(TenantId, ReferenceType, ReferenceId)</c> — ese índice da idempotencia a la reserva y permite
/// encontrar el hold vivo al liquidar. El Wallet no conoce al consumidor: solo la referencia opaca.
/// </summary>
public interface IReservationRepository
{
    /// <summary>La reserva de esa referencia, o <c>null</c> si aún no reservó fondos.</summary>
    Task<WalletReservation?> GetByReferenceAsync(
        Guid tenantId,
        string referenceType,
        Guid referenceId,
        CancellationToken ct = default
    );

    Task AddAsync(WalletReservation reservation, CancellationToken ct = default);
}
