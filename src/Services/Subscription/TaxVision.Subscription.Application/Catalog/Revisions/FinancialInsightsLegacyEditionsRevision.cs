using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// Pedido del dueño: toda oficina de Enterprise o Pro recibe Financial Insights, también las que
/// están en una edición vieja con otros términos (la revisión del módulo las saltaba para no cambiarles precio ni límites). A cada edición
/// vieja se le hace una edición heredada con el módulo y los mismos términos, y sus oficinas se re-anclan ahí: ganan el
/// módulo y nada más cambia. Idempotente.
/// </summary>
public sealed class FinancialInsightsLegacyEditionsRevision : ICatalogRevision
{
    private const string ModuleCode = FinancialInsightsModuleRevision.ModuleCode;
    private static readonly string[] Plans = ["enterprise", "pro"];

    public string Id => "2026-11-13-financial-insights-legacy-editions";

    public string Description => "Give Financial Insights to Enterprise and Pro offices on older editions, keeping their terms.";

    public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
    {
        if (context.Modules is null)
            return Fail("CatalogRevision.ModulesUnavailable", "The module catalog is not available to the revision.");

        var catalog = await context.Modules.GetAllAsync(ct);
        var changes = new List<string>();
        foreach (var code in Plans)
        {
            var listed = (await context.Plans.GetAllAsync(ct)).FirstOrDefault(plan => plan.Code.Value == code);
            var plan = listed is null ? null : await context.Plans.GetByIdForUpdateAsync(listed.Id, ct);
            if (plan is null)
            {
                changes.Add($"{code}: plan not found");
                continue;
            }

            var before = CatalogAuditViews.Of(plan);
            var summary = await ApplyToPlanAsync(plan, catalog, context, ct);
            if (summary.IsFailure)
                return summary;
            changes.Add($"{code}: {summary.Value}");

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
        }

        return Result.Success(string.Join("; ", changes));
    }

    private static async Task<Result<string>> ApplyToPlanAsync(
        SubscriptionPlan plan,
        IReadOnlyCollection<Module> catalog,
        CatalogRevisionContext context,
        CancellationToken ct
    )
    {
        var legacyBySource = new Dictionary<Guid, SubscriptionPlanVersion>();
        var created = 0;
        var moved = 0;
        foreach (var subscription in await context.Subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct))
        {
            var current = plan.Versions.FirstOrDefault(version => version.Id == subscription.PlanVersionId);
            if (current is null || current.Status != PlanVersionStatus.Superseded)
                continue;
            var currentModules = ContractedEntitlementPolicy.EnabledModules(current, catalog);
            if (currentModules.IsFailure || currentModules.Value.Contains(ModuleCode, StringComparer.OrdinalIgnoreCase))
                continue;

            if (!legacyBySource.TryGetValue(current.Id, out var legacy))
            {
                legacy = FindLegacy(plan, current, currentModules.Value, catalog)!;
                if (legacy is null)
                {
                    var added = plan.AddLegacyEditionWithModules(
                        current.Id,
                        [ModuleCode],
                        CatalogRevisionContext.DeployActor,
                        context.NowUtc
                    );
                    if (added.IsFailure)
                        return Result.Failure<string>(added.Error);
                    legacy = added.Value;
                    created++;
                }
                legacyBySource[current.Id] = legacy;
            }

            var from = subscription.PlanVersionId;
            var rebaselined = subscription.RebaselineVersion(
                plan,
                legacy.Id,
                "Financial Insights legacy edition",
                CatalogRevisionContext.DeployActor,
                context.NowUtc
            );
            if (rebaselined.IsFailure)
                return Result.Failure<string>(rebaselined.Error);

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
                new { PlanCode = plan.Code.Value, PlanVersionId = legacy.Id },
                context.CorrelationId,
                context.NowUtc,
                ct
            );
        }

        return Result.Success($"legacy editions {created}; offices moved {moved}");
    }

    /// <summary>Una edición heredada ya creada para esa edición: mismos términos y exactamente sus módulos + el nuevo.</summary>
    private static SubscriptionPlanVersion? FindLegacy(
        SubscriptionPlan plan,
        SubscriptionPlanVersion source,
        IReadOnlyCollection<string> sourceModules,
        IReadOnlyCollection<Module> catalog
    )
    {
        var wanted = sourceModules.Append(ModuleCode).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var terms = CommercialTerms.Of(source);
        return plan.Versions.FirstOrDefault(candidate =>
            candidate.Id != source.Id
            && candidate.Status == PlanVersionStatus.Superseded
            && CommercialTerms.Of(candidate) == terms
            && ContractedEntitlementPolicy.EnabledModules(candidate, catalog) is { IsSuccess: true } modules
            && wanted.SetEquals(modules.Value)
        );
    }

    private static Result<string> Fail(string code, string message) => Result.Failure<string>(new Error(code, message));
}
