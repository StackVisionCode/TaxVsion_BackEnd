namespace TaxVision.Wallet.Domain.Wallet;

/// <summary>
/// Tipo de movimiento del ledger (00_Plan §3, modelo v3 de dos deltas). Cada movimiento tiene un efecto
/// fijo sobre (ΔPosted, ΔHeld):
/// <list type="bullet">
/// <item><see cref="TopUp"/>      (+a,  0) — acredita un pago confirmado.</item>
/// <item><see cref="Reserve"/>    ( 0, +a) — aparta saldo disponible (hold).</item>
/// <item><see cref="Consume"/>    (−a, −a) — cobra parte de una reserva.</item>
/// <item><see cref="Release"/>    ( 0, −a) — libera reserva no usada (NO es un refund).</item>
/// <item><see cref="UsageRefund"/>(+a,  0) — devuelve un consumo ya cobrado.</item>
/// <item><see cref="Adjustment"/> (±a,  0) — corrección económica autorizada, con motivo.</item>
/// </list>
/// </summary>
public enum MovementType
{
    TopUp = 1,
    Reserve = 2,
    Consume = 3,
    Release = 4,
    UsageRefund = 5,
    Adjustment = 6,
}
