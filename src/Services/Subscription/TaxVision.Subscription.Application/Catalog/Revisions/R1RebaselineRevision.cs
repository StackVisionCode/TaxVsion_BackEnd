using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Catalog.Revisions;

/// <summary>
/// R1: ancla cada oficina a la edición publicada de su plan cuando tiene los mismos términos comerciales que su
/// edición actual. Conserva precio y límites, y gana los módulos y claves vigentes. Las de términos distintos no
/// se tocan: quedan en el resumen. Corre antes de R2 (F2 lee la edición contratada).
/// </summary>
public sealed class R1RebaselineRevision : ICatalogRevision
{
    public string Id => "2026-11-11-r1-rebaseline";

    public string Description => "Anchor every office to the published edition of its plan when the terms match.";

    public async Task<Result<string>> ApplyAsync(CatalogRevisionContext context, CancellationToken ct)
    {
        var rebaselined = 0;
        var skipped = new List<string>();

        foreach (var listed in await context.Plans.GetAllAsync(ct))
        {
            var plan = await context.Plans.GetByIdForUpdateAsync(listed.Id, ct);
            var published = plan?.GetPublishedVersion();
            if (plan is null || published is null)
                continue;

            foreach (var subscription in await context.Subscriptions.ListByPlanAcrossTenantsAsync(plan.Id, ct))
            {
                if (subscription.PlanVersionId == published.Id)
                    continue;

                var from = subscription.PlanVersionId;
                var result = subscription.RebaselineVersion(
                    plan,
                    published.Id,
                    "R1 rebaseline",
                    CatalogRevisionContext.DeployActor,
                    context.NowUtc
                );
                if (result.IsSuccess)
                {
                    rebaselined++;
                    // Queda en el historial de la oficina: otra ancla, mismos términos, quién y por qué.
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
                else
                    skipped.Add($"{subscription.TenantId} ({plan.Code.Value}: {result.Error.Code})");
            }
        }

        var summary = $"rebaselined {rebaselined}; skipped {skipped.Count}";
        return Result.Success(skipped.Count == 0 ? summary : $"{summary}: {string.Join(", ", skipped)}");
    }
}
