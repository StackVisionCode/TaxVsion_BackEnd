using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Plans.Commands.CreateDraftVersion;

public static class CreateDraftVersionHandler
{
    public static async Task<Result<CreateDraftVersionResult>> Handle(
        CreateDraftVersionCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
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

        var nextVersion = plan.Versions.Count == 0 ? 1 : plan.Versions.Max(v => v.VersionNumber) + 1;
        var versionResult = SubscriptionPlanVersion.Create(plan.Id, nextVersion, command.TrialDaysDefault, cycles);
        if (versionResult.IsFailure)
            return Result.Failure<CreateDraftVersionResult>(versionResult.Error);

        var nowUtc = DateTime.UtcNow;
        var addResult = plan.AddVersion(versionResult.Value, command.ActorUserId, nowUtc);
        if (addResult.IsFailure)
            return Result.Failure<CreateDraftVersionResult>(addResult.Error);

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new CreateDraftVersionResult(versionResult.Value.Id, nextVersion));
    }
}
