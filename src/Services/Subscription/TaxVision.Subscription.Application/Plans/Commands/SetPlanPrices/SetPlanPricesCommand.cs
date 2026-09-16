namespace TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;

/// <summary>Revisa el precio mensual/anual del plan (versión nueva publicada). Afecta compras/renovaciones
/// futuras; las vigentes conservan su precio. PlatformAdmin.</summary>
public sealed record SetPlanPricesCommand(Guid PlanId, decimal MonthlyUsd, decimal YearlyUsd, Guid ActorUserId);
