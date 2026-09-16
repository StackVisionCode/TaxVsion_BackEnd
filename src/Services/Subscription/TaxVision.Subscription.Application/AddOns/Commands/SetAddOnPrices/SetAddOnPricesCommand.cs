namespace TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;

/// <summary>Actualiza el precio mensual/anual de un add-on. Afecta compras nuevas; las vigentes conservan su
/// precio. PlatformAdmin.</summary>
public sealed record SetAddOnPricesCommand(
    Guid AddOnDefinitionId,
    decimal MonthlyUsd,
    decimal YearlyUsd,
    Guid ActorUserId
);
