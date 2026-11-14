using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Plans;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// Financial Insights es un módulo comercial. Entra a Enterprise y Pro con una edición nueva, se
/// vende como add-on ($29/$290) a los demás planes, y quien ya es cliente de Enterprise o Pro lo recibe de inmediato
/// porque solo suma (D11): se re-ancla a la edición nueva si los términos son iguales y no pierde ningún módulo.
/// Idempotente.
/// </summary>
public sealed class FinancialInsightsModuleRevision : ICatalogRevision
{
    public const string ModuleCode = "financial_insights";
    public const decimal AddonMonthlyUsd = 29m;
    public const decimal AddonYearlyUsd = 290m;

    private static readonly string[] IncludedIn = ["enterprise", "pro"];

    public string Id => "2026-11-13-r4-financial-insights";

    public string Description => "Add Financial Insights to Enterprise and Pro and sell it as an add-on.";

    public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
    {
        if (context.Modules is null)
            return Fail("CatalogRevision.ModulesUnavailable", "The module catalog is not available to the revision.");

        var module = await context.Modules.GetByCodeForUpdateAsync(ModuleCode, ct);
        if (module is null)
            return Fail("CatalogRevision.ModuleMissing", $"Module '{ModuleCode}' does not exist.");

        var changes = new List<string>();
        var priced = await PriceAsAddonAsync(module, context, ct);
        if (priced.IsFailure)
            return Result.Failure<string>(priced.Error);
        changes.Add(priced.Value);

        var catalog = await context.Modules.GetAllAsync(ct);
        foreach (var code in IncludedIn)
        {
            var plan = await FindPlanAsync(context, code, ct);
            if (plan is null)
            {
                changes.Add($"{code}: plan not found");
                continue;
            }

            var included = await IncludeAsync(plan, catalog, context, ct);
            if (included.IsFailure)
                return Result.Failure<string>(included.Error);
            changes.Add($"{code}: {included.Value}");

            changes.Add($"{code}: {await ReanchorAsync(plan, catalog, context, ct)}");
        }

        return Result.Success(string.Join("; ", changes));
    }

    private static async Task<Result<string>> PriceAsAddonAsync(
        Module module,
        CatalogRevisionContext context,
        CancellationToken ct
    )
    {
        if (module.IsSellableAsAddon)
            return Result.Success("add-on already priced");

        var before = new { module.Category, module.AddonPriceMonthlyUsd, module.AddonPriceYearlyUsd };
        var commercial = module.MarkAsCommercial();
        if (commercial.IsFailure)
            return Result.Failure<string>(commercial.Error);
        var addon = module.SetAsAddon(AddonMonthlyUsd, AddonYearlyUsd);
        if (addon.IsFailure)
            return Result.Failure<string>(addon.Error);

        await CatalogAudit.RecordAsync(
            context.CatalogAudit,
            context.Actor,
            "module.addon_price",
            new CatalogAuditTarget(nameof(Module), module.Id, module.Code),
            before,
            new { module.Category, module.AddonPriceMonthlyUsd, module.AddonPriceYearlyUsd },
            reason: context.CorrelationId,
            context.CorrelationId,
            context.NowUtc,
            ct
        );
        return Result.Success($"add-on priced ${AddonMonthlyUsd}/${AddonYearlyUsd}");
    }

    private static async Task<SubscriptionPlan?> FindPlanAsync(
        CatalogRevisionContext context,
        string code,
        CancellationToken ct
    )
    {
        var listed = (await context.Plans.GetAllAsync(ct)).FirstOrDefault(plan => plan.Code.Value == code);
        return listed is null ? null : await context.Plans.GetByIdForUpdateAsync(listed.Id, ct);
    }

    /// <summary>Publica una edición nueva con el módulo si la publicada no lo trae; lo demás se conserva.</summary>
    private static async Task<Result<string>> IncludeAsync(
        SubscriptionPlan plan,
        IReadOnlyCollection<Module> catalog,
        CatalogRevisionContext context,
        CancellationToken ct
    )
    {
        var published = plan.GetPublishedVersion();
        if (published is null)
            return Result.Success("no published edition");

        var enabled = ContractedEntitlementPolicy.EnabledModules(published, catalog);
        if (enabled.IsFailure)
            return Result.Failure<string>(enabled.Error);
        if (enabled.Value.Contains(ModuleCode, StringComparer.OrdinalIgnoreCase))
            return Result.Success("already included");

        var commercial = enabled
            .Value.Where(code =>
                catalog.Any(module =>
                    string.Equals(module.Code, code, StringComparison.OrdinalIgnoreCase)
                    && module.Category == ModuleCategory.Commercial
                )
            )
            .Append(ModuleCode)
            .ToArray();
        var selection = await PlanModuleSelection.ComposeAsync(commercial, context.Modules!, ct);
        if (selection.IsFailure)
            return Result.Failure<string>(selection.Error);

        var before = CatalogAuditViews.Of(plan);
        var revised = plan.ReviseModules(selection.Value, CatalogRevisionContext.DeployActor, context.NowUtc);
        if (revised.IsFailure)
            return Result.Failure<string>(revised.Error);

        await CatalogAudit.RecordIfChangedAsync(
            context.CatalogAudit,
            context.Actor,
            "catalog.revision",
            CatalogAuditViews.Target(plan),
            before,
            CatalogAuditViews.Of(plan),
            reason: context.CorrelationId,
            context.CorrelationId,
            context.NowUtc,
            ct
        );
        return Result.Success($"edition v{plan.GetPublishedVersion()!.VersionNumber} with {ModuleCode}");
    }

    /// <summary>Mueve a la edición vigente a cada oficina que solo gana: mismos términos y ningún módulo menos.</summary>
    private static async Task<string> ReanchorAsync(
        SubscriptionPlan plan,
        IReadOnlyCollection<Module> catalog,
        CatalogRevisionContext context,
        CancellationToken ct
    )
    {
        var published = plan.GetPublishedVersion();
        if (published is null)
            return "re-anchored 0; skipped 0";

        var target = ContractedEntitlementPolicy.EnabledModules(published, catalog);
        var moved = 0;
        var skipped = 0;
        foreach (var subscription in await context.Subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct))
        {
            if (subscription.PlanVersionId == published.Id)
                continue;

            var current = plan.Versions.FirstOrDefault(version => version.Id == subscription.PlanVersionId);
            var currentModules = current is null ? null : ContractedEntitlementPolicy.EnabledModules(current, catalog);
            var losesModule =
                target.IsFailure
                || currentModules is null
                || currentModules.IsFailure
                || currentModules.Value.Except(target.Value, StringComparer.OrdinalIgnoreCase).Any();
            if (losesModule)
            {
                skipped++;
                continue;
            }

            var from = subscription.PlanVersionId;
            var result = subscription.RebaselineVersion(
                plan,
                published.Id,
                "Financial Insights",
                CatalogRevisionContext.DeployActor,
                context.NowUtc
            );
            if (result.IsFailure)
            {
                skipped++;
                continue;
            }

            moved++;
            await AuditEntryFactory.AppendAsync(
                context.TenantAudit,
                subscription.TenantId,
                nameof(TenantSubscription),
                subscription.Id,
                "subscription.version_rebaselined",
                CatalogRevisionContext.DeployActor,
                context.CorrelationId,
                new { PlanCode = plan.Code.Value, PlanVersionId = from },
                new { PlanCode = plan.Code.Value, PlanVersionId = published.Id },
                context.CorrelationId,
                context.NowUtc,
                ct
            );
        }

        return $"re-anchored {moved}; skipped {skipped}";
    }

    private static Result<string> Fail(string code, string message) => Result.Failure<string>(new Error(code, message));
}
