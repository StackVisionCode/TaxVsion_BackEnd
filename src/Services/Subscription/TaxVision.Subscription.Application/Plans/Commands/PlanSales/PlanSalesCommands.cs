namespace TaxVision.Subscription.Application.Plans.Commands.PlanSales;

public sealed record PutPlanOnSaleCommand(Guid PlanId, Guid ActorUserId);

/// <summary>Operación peligrosa: el token de la vista previa y un motivo.</summary>
public sealed record WithdrawPlanFromSaleCommand(
    Guid PlanId,
    Guid ActorUserId,
    string? ConfirmationToken,
    string? Reason
);

public sealed record SetPlanSalesRankCommand(Guid PlanId, int Rank, Guid ActorUserId);

public sealed record SetPlanFeaturedCommand(Guid PlanId, bool IsFeatured, Guid ActorUserId);
