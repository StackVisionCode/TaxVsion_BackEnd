namespace TaxVision.Subscription.Application.Seats.Queries;

/// <summary>Cotización de compra de asientos. Montos en centavos (enteros) para evitar redondeo en el
/// transporte. <see cref="ProratedUnitAmountCents"/> es lo que se cobra HOY por asiento (prorrateado a lo
/// que resta del período vigente); <see cref="UnitAmountCents"/> es el precio de período completo que se
/// cobrará en cada renovación.</summary>
public sealed record SeatQuoteResponse(
    string SeatType,
    int Quantity,
    string BillingCycle,
    long UnitAmountCents,
    long ProratedUnitAmountCents,
    long ProratedTotalCents,
    string Currency,
    DateTime CurrentPeriodEndUtc,
    // Descuento vigente sobre este cobro (null si no aplica) y lo que se paga con él: lo mismo que cobra el checkout.
    long? DiscountCents = null,
    long? NetTotalCents = null
);
