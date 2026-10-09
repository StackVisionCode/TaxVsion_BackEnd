namespace TaxVision.Wallet.Domain.Reservations;

/// <summary>
/// Estado de una <see cref="WalletReservation"/> (PEP money-OUT, 00_Plan §5). Una reserva abierta tiene
/// fondos apartados (<c>Held</c>); al cerrarse el run se liquida (consume lo usado + libera el resto) y
/// queda <see cref="Settled"/>. No hay vuelta atrás: una reserva liquidada es inmutable.
/// </summary>
public enum ReservationStatus
{
    /// <summary>Fondos apartados (hold vivo). Esperando el cierre del run para liquidar.</summary>
    Open = 1,

    /// <summary>Liquidada: se consumió lo usado y se liberó el remanente. Inmutable.</summary>
    Settled = 2,
}
