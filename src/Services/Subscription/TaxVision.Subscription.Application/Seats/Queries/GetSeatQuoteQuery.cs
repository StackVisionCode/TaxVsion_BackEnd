namespace TaxVision.Subscription.Application.Seats.Queries;

/// <summary>Cotización server-authoritative para comprar <paramref name="Quantity"/> asientos de un tipo:
/// el precio unitario del catálogo GLOBAL y el prorrateo al período vigente de la base (el mismo cálculo
/// que hace la compra). El precio nunca se computa en el cliente.</summary>
public sealed record GetSeatQuoteQuery(Guid TenantId, string SeatType, int Quantity);
