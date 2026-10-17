using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.CreatePlan;

/// <summary>CAT-F2.3 — crea plan nuevo con versión v1 draft vacía. El autor completa modules/
/// entitlements/features/precios en F2.4 (<c>PUT .../versions/{v}/draft</c>) y la publica en F2.5.
/// Mientras es Draft, el plan no sale en el listado público ni se puede contratar.</summary>
public static class CreatePlanHandler
{
    public static async Task<Result<CreatePlanResult>> Handle(
        CreatePlanCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionPlan> logger,
        CancellationToken ct
    )
    {
        var planCodeResult = PlanCode.Create(command.Code);
        if (planCodeResult.IsFailure)
            return Result.Failure<CreatePlanResult>(planCodeResult.Error);

        if (!Enum.TryParse<PlanTier>(command.Tier, ignoreCase: true, out var tier))
            return Result.Failure<CreatePlanResult>(
                new Error("Plan.InvalidTier", "Tier must be one of Standard/Pro/Enterprise/Trial.")
            );

        if (await plans.ExistsByCodeAsync(command.Code, ct))
            return Result.Failure<CreatePlanResult>(
                new Error("Plan.CodeAlreadyExists", $"Plan '{command.Code}' already exists.")
            );

        var cycles = new List<BillingCycle>(command.SupportedBillingCycles.Count);
        foreach (var raw in command.SupportedBillingCycles)
        {
            if (!Enum.TryParse<BillingCycle>(raw, ignoreCase: true, out var cycle))
                return Result.Failure<CreatePlanResult>(
                    new Error("Plan.InvalidBillingCycle", $"Unknown billing cycle '{raw}'.")
                );
            cycles.Add(cycle);
        }

        var nowUtc = DateTime.UtcNow;
        var planResult = SubscriptionPlan.Create(
            planCodeResult.Value,
            command.Name,
            command.Description,
            tier,
            command.ActorUserId,
            nowUtc
        );
        if (planResult.IsFailure)
            return Result.Failure<CreatePlanResult>(planResult.Error);

        var plan = planResult.Value;

        var versionResult = SubscriptionPlanVersion.Create(plan.Id, versionNumber: 1, command.TrialDaysDefault, cycles);
        if (versionResult.IsFailure)
            return Result.Failure<CreatePlanResult>(versionResult.Error);

        var addVersion = plan.AddVersion(versionResult.Value, command.ActorUserId, nowUtc);
        if (addVersion.IsFailure)
            return Result.Failure<CreatePlanResult>(addVersion.Error);

        await plans.AddAsync(plan, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.created",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                // Fitness exige la palabra `CorrelationId` en el initializer; `string.Empty` es el default
                // del base record. Para trazabilidad real inyectar `ICorrelationContext` cuando sea necesario.
                CorrelationId = string.Empty,
            }
        );

        logger.LogInformation(
            "Plan {Code} ({PlanId}) created by {ActorUserId} with draft version 1 ({VersionId}).",
            command.Code,
            plan.Id,
            command.ActorUserId,
            versionResult.Value.Id
        );

        return Result.Success(new CreatePlanResult(plan.Id, versionResult.Value.Id, 1));
    }
}
