using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Catalog.Confirmation;

/// <summary>Operaciones del catálogo que necesitan vista previa, token de confirmación y motivo (D13).</summary>
public enum PlanOperation
{
    WithdrawFromSale,
    Deprecate,
    Archive,
}

public sealed record PreviewPlanOperationQuery(Guid PlanId, PlanOperation Operation);

/// <summary>
/// Lo que va a pasar si se confirma: cuántas oficinas tienen el plan y qué lo impediría. El token amarra la
/// confirmación a este estado: si algo cambia antes de confirmar, deja de valer.
/// </summary>
public sealed record PlanOperationPreview(
    Guid PlanId,
    string PlanCode,
    string Operation,
    int OfficesOnPlan,
    IReadOnlyList<string> Blockers,
    string ConfirmationToken
);

/// <summary>La vista previa no cambia nada.</summary>
public static class PreviewPlanOperationHandler
{
    public static async Task<Result<PlanOperationPreview>> Handle(
        PreviewPlanOperationQuery query,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        IOptions<SubscriptionOptions> options,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdAsync(query.PlanId, ct);
        if (plan is null)
            return Result.Failure<PlanOperationPreview>(new Error("Plan.NotFound", "Plan not found."));

        return Result.Success(
            await PlanOperationConfirmation.PreviewAsync(plan, query.Operation, plans, subscriptions, options.Value, ct)
        );
    }
}

/// <summary>
/// Reglas comunes de las operaciones peligrosas: motivo obligatorio, token vigente y bloqueos. El token es un hash
/// del estado que se mostró (operación, plan, su <c>RowVersion</c> y su estado de venta, y la cantidad de
/// oficinas): no es un secreto, es la prueba de que se confirma lo que se vio.
/// </summary>
public static class PlanOperationConfirmation
{
    public static async Task<PlanOperationPreview> PreviewAsync(
        SubscriptionPlan plan,
        PlanOperation operation,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        SubscriptionOptions options,
        CancellationToken ct
    )
    {
        var offices = (await subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct)).Count;
        var blockers = new List<string>();

        if ((operation is PlanOperation.WithdrawFromSale or PlanOperation.Deprecate) && plan.IsOnSale)
        {
            var onSale = await plans.GetOnSaleAsync(ct);
            var rule = PlanSalesRules.EnsureCanWithdraw(
                plan,
                onSale.Count(p => p.Id != plan.Id),
                options.DefaultPlanCode
            );
            if (rule.IsFailure)
                blockers.Add(rule.Error.Code);
        }

        if (operation == PlanOperation.Deprecate && plan.Status == PlanStatus.Archived)
            blockers.Add("Plan.AlreadyArchived");

        if (operation == PlanOperation.Archive)
        {
            if (plan.Status != PlanStatus.Deprecated)
                blockers.Add("Plan.NotDeprecated");
            if (offices > 0)
                blockers.Add("Catalog.PlanHasSubscribers");
        }

        var token = CatalogConfirmation.Token(
            operation,
            plan.Id,
            Convert.ToHexString(plan.RowVersion),
            plan.Status,
            plan.IsOnSale,
            plan.SalesRank.Value,
            plan.IsFeatured,
            plan.GetPublishedVersion()?.Id,
            offices
        );

        return new PlanOperationPreview(plan.Id, plan.Code.Value, operation.ToString(), offices, blockers, token);
    }

    /// <summary>Motivo y token presentes; se valida antes de leer nada.</summary>
    public static Result ValidateRequest(string? confirmationToken, string? reason) =>
        CatalogConfirmation.ValidateRequest(confirmationToken, reason);

    /// <summary>El token tiene que ser el de la vista previa del estado actual; después, ningún bloqueo.</summary>
    public static async Task<Result> ConfirmAsync(
        SubscriptionPlan plan,
        PlanOperation operation,
        string confirmationToken,
        IPlanRepository plans,
        IPlanSubscriptionsAcrossTenants subscriptions,
        SubscriptionOptions options,
        CancellationToken ct
    )
    {
        var current = await PreviewAsync(plan, operation, plans, subscriptions, options, ct);
        if (!CatalogConfirmation.Matches(current.ConfirmationToken, confirmationToken))
            return Result.Failure(CatalogConfirmation.Stale);

        return current.Blockers.Count == 0
            ? Result.Success()
            : Result.Failure(new Error(current.Blockers[0], BlockerMessage(current.Blockers[0])));
    }

    private static string BlockerMessage(string code) =>
        code switch
        {
            "Plan.LastOnSale" => "At least one plan must stay on sale. Put another plan on sale first.",
            "Plan.IsTrialPlan" => "New offices start their trial on this plan, so it must stay on sale.",
            "Plan.AlreadyArchived" => "Plan is already archived.",
            "Plan.NotDeprecated" => "Only a deprecated plan can be archived.",
            "Catalog.PlanHasSubscribers" => "Offices are still on this plan; move them before archiving it.",
            _ => code,
        };
}
