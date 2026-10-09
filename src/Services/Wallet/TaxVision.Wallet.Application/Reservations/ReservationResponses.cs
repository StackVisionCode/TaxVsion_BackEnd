namespace TaxVision.Wallet.Application.Reservations;

/// <summary>
/// Resultado de reservar fondos para una referencia (PEP, <c>POST /internal/wallet/reservations</c>). Siempre
/// 200: <see cref="Authorized"/> dice si se apartaron los fondos; si no, <see cref="DeficitMicros"/> lleva el
/// faltante para que el consumidor muestre "faltan $X" al instante.
/// </summary>
public sealed record ReservationView(
    bool Authorized,
    Guid? ReservationId,
    int PriceBookVersion,
    long CostMicros,
    long AvailableMicros,
    long DeficitMicros,
    string Currency
);

/// <summary>Resultado de liquidar una reserva (<c>POST /internal/wallet/reservations/settle</c>).</summary>
public sealed record SettlementView(long ConsumedMicros, long ReleasedMicros, string Currency);
