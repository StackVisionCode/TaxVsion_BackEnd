namespace TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;

/// <summary>Actualiza el precio mensual/anual de un tipo de asiento en el catálogo GLOBAL. Afecta compras
/// nuevas; las vigentes conservan su precio. PlatformAdmin.</summary>
public sealed record SetSeatPricesCommand(string SeatType, decimal MonthlyUsd, decimal YearlyUsd, Guid ActorUserId);
