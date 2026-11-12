using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// R2 (pedido del 2026-11-11): un solo plan a la venta, Enterprise a $99/$999 con 3 asientos y 15 GB. Pro y Starter
/// quedan definidos con sus términos nuevos pero fuera de la venta; cualquier otro plan también sale de la venta.
/// Lo demás de cada edición se copia (D10). Quien ya es cliente conserva su edición (D4). Idempotente.
/// </summary>
public sealed class R2SingleEnterpriseRevision : ICatalogRevision
{
    private const long GiB = 1024L * 1024 * 1024;
    private const string Enterprise = "enterprise";

    private static readonly Target[] Targets =
    [
        new(Enterprise, 99m, 999m, Seats: 3, StorageBytes: 15 * GiB, OnSale: true, Rank: 3, Featured: true),
        new("pro", 69m, 699m, Seats: 2, StorageBytes: 10 * GiB, OnSale: false, Rank: 2, Featured: false),
        new("starter", 39m, 399m, Seats: 1, StorageBytes: 5 * GiB, OnSale: false, Rank: 1, Featured: false),
    ];

    public string Id => "2026-11-11-r2-single-enterprise";

    public string Description => "Sell only Enterprise ($99/$999, 3 seats, 15 GB); keep Pro and Starter defined.";

    public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
    {
        // El trial de las oficinas nuevas tiene que estar a la venta: con otro plan, no se toca nada.
        if (!string.Equals(context.Options.DefaultPlanCode, Enterprise, StringComparison.OrdinalIgnoreCase))
            return Fail(
                "CatalogRevision.TrialPlanMustBeEnterprise",
                $"Subscriptions:DefaultPlanCode is '{context.Options.DefaultPlanCode}'; set it to '{Enterprise}' first."
            );

        var plans = new List<SubscriptionPlan>();
        foreach (var listed in await context.Plans.GetAllAsync(ct))
        {
            var tracked = await context.Plans.GetByIdForUpdateAsync(listed.Id, ct);
            if (tracked is not null)
                plans.Add(tracked);
        }

        var missing = Targets.FirstOrDefault(target => plans.All(plan => plan.Code.Value != target.Code));
        if (missing is not null)
            return Fail("CatalogRevision.PlanMissing", $"Plan '{missing.Code}' does not exist.");

        // La foto de cada plan antes de tocar nada, para la auditoría.
        var before = plans.ToDictionary(plan => plan.Id, CatalogAuditViews.Of);

        // Enterprise primero: así retirar los demás nunca deja el catálogo sin un plan a la venta.
        var changes = new List<string>();
        foreach (var target in Targets)
        {
            var applied = Apply(plans.First(plan => plan.Code.Value == target.Code), target, plans, context);
            if (applied.IsFailure)
                return Result.Failure<string>(applied.Error);
            changes.Add($"{target.Code}: {applied.Value}");
        }

        foreach (var other in plans.Where(plan => plan.IsOnSale && Targets.All(t => t.Code != plan.Code.Value)))
        {
            var withdrawn = Withdraw(other, plans, context);
            if (withdrawn.IsFailure)
                return Result.Failure<string>(withdrawn.Error);
            changes.Add($"{other.Code.Value}: off sale");
        }

        foreach (var plan in plans)
            await CatalogAudit.RecordIfChangedAsync(
                context.CatalogAudit,
                context.Actor,
                "catalog.revision",
                CatalogAuditViews.Target(plan),
                before[plan.Id],
                CatalogAuditViews.Of(plan),
                reason: context.CorrelationId,
                context.CorrelationId,
                context.NowUtc,
                ct
            );

        return Result.Success(string.Join("; ", changes));
    }

    private static Result<string> Apply(
        SubscriptionPlan plan,
        Target target,
        IReadOnlyCollection<SubscriptionPlan> plans,
        CatalogRevisionContext context
    )
    {
        var published = plan.GetPublishedVersion();
        if (published is null)
            return Fail("CatalogRevision.NoPublishedVersion", $"Plan '{target.Code}' has no published edition.");

        var terms = CommercialTerms
            .Of(published)
            .WithPrices(Money.Create(target.Monthly, "USD").Value, Money.Create(target.Yearly, "USD").Value)
            .WithSeatsMax(target.Seats)
            .WithStorageMaxBytes(target.StorageBytes);
        var revised = plan.ReviseCommercialTerms(terms, CatalogRevisionContext.DeployActor, context.NowUtc);
        if (revised.IsFailure)
            return Result.Failure<string>(revised.Error);

        var sale = target.OnSale
            ? plan.PutOnSale(CatalogRevisionContext.DeployActor, context.NowUtc)
            : Withdraw(plan, plans, context);
        if (sale.IsFailure)
            return Result.Failure<string>(sale.Error);

        var rank = plan.SetSalesRank(
            SalesRank.Create(target.Rank).Value,
            CatalogRevisionContext.DeployActor,
            context.NowUtc
        );
        if (rank.IsFailure)
            return Result.Failure<string>(rank.Error);

        var featured = plan.SetFeatured(target.Featured, CatalogRevisionContext.DeployActor, context.NowUtc);
        if (featured.IsFailure)
            return Result.Failure<string>(featured.Error);

        var edition = revised.Value ? $"edition v{plan.GetPublishedVersion()!.VersionNumber}" : "terms already current";
        return Result.Success($"{edition}, {(target.OnSale ? "on sale" : "off sale")}, rank {target.Rank}");
    }

    private static Result Withdraw(
        SubscriptionPlan plan,
        IReadOnlyCollection<SubscriptionPlan> plans,
        CatalogRevisionContext context
    )
    {
        var others = plans.Count(other => other.Id != plan.Id && other.IsOnSale);
        var allowed = PlanSalesRules.EnsureCanWithdraw(plan, others, context.Options.DefaultPlanCode);
        return allowed.IsFailure ? allowed : plan.WithdrawFromSale(CatalogRevisionContext.DeployActor, context.NowUtc);
    }

    private static Result<string> Fail(string code, string message) => Result.Failure<string>(new Error(code, message));

    private sealed record Target(
        string Code,
        decimal Monthly,
        decimal Yearly,
        int Seats,
        long StorageBytes,
        bool OnSale,
        int Rank,
        bool Featured
    );
}
