using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Plans.Commands.CreateDraftVersion;

public static class CreateDraftVersionHandler
{
    public static async Task<Result<CreateDraftVersionResult>> Handle(
        CreateDraftVersionCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure<CreateDraftVersionResult>(new Error("Plan.NotFound", "Plan not found."));

        if (plan.Versions.Any(v => v.Status == PlanVersionStatus.Draft))
            return Result.Failure<CreateDraftVersionResult>(
                new Error(
                    "Plan.DraftAlreadyExists",
                    "Plan already has a draft version. Publish or discard it before opening another."
                )
            );

        var cycles = new List<BillingCycle>(command.SupportedBillingCycles.Count);
        foreach (var raw in command.SupportedBillingCycles)
        {
            if (!Enum.TryParse<BillingCycle>(raw, ignoreCase: true, out var cycle))
                return Result.Failure<CreateDraftVersionResult>(
                    new Error("Plan.InvalidBillingCycle", $"Unknown billing cycle '{raw}'.")
                );
            cycles.Add(cycle);
        }

        // El borrador nace como copia de la edición publicada: se cambia solo lo necesario.
        var nowUtc = DateTime.UtcNow;
        var versionResult = plan.OpenDraft(command.TrialDaysDefault, cycles, command.ActorUserId, nowUtc);
        if (versionResult.IsFailure)
            return Result.Failure<CreateDraftVersionResult>(versionResult.Error);
        var nextVersion = versionResult.Value.VersionNumber;

        await CatalogAudit.RecordAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "plan.draft_created",
            CatalogAuditViews.Target(plan),
            before: null,
            CatalogAuditViews.Draft(versionResult.Value),
            reason: null,
            correlation.CorrelationId,
            nowUtc,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new CreateDraftVersionResult(versionResult.Value.Id, nextVersion));
    }
}
