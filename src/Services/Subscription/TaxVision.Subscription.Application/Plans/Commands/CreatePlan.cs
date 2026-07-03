using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Commands;

public sealed record CreatePlanCommand(
    string Name,
    string Title,
    string Description,
    decimal BasePriceMonthly,
    decimal BasePriceAnnual,
    decimal PricePerAdditionalSeat,
    int IncludedSeats,
    string Currency,
    bool IsActive,
    ServiceLevel ServiceLevel,
    List<string> Features);

public static class CreatePlanHandler
{
    public static async Task<Result<PlanDto>> Handle(
        CreatePlanCommand cmd,
        IPlanRepository repo,
        IPlanReadService readService,
        IUnitOfWork uow,
        ILogger<CreatePlanCommand> logger,
        CancellationToken ct)
    {
        if (await repo.ExistsWithNameAsync(cmd.Name, ct))
            return Result.Failure<PlanDto>(new Error("Plan.NameConflict", $"Plan name '{cmd.Name}' already exists."));

        if (cmd.BasePriceMonthly < 0)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidPrice", "Monthly price cannot be negative."));

        if (cmd.BasePriceAnnual > 0 && cmd.BasePriceAnnual > cmd.BasePriceMonthly * 12)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidPrice", "Annual price cannot exceed monthly * 12."));

        if (cmd.IncludedSeats < 0)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidSeats", "Included seats cannot be negative."));

        var features = cmd.Features
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (features.Count == 0)
            return Result.Failure<PlanDto>(new Error("Plan.NoFeatures", "At least one feature is required."));

        var createResult = Plan.Create(
            code: cmd.Name.ToLowerInvariant().Replace(" ", "-"),
            name: cmd.Name,
            description: cmd.Description,
            basePriceMonthly: cmd.BasePriceMonthly,
            basePriceAnnual: cmd.BasePriceAnnual,
            pricePerAdditionalSeat: cmd.PricePerAdditionalSeat,
            includedSeats: cmd.IncludedSeats,
            currency: cmd.Currency,
            featureCodes: features,
            title: cmd.Title,
            serviceLevel: cmd.ServiceLevel);

        if (createResult.IsFailure)
            return Result.Failure<PlanDto>(createResult.Error);

        var plan = createResult.Value;
        await repo.AddAsync(plan, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Plan created: {PlanId} ({Name})", plan.Id, plan.Name);
        return await readService.GetByIdWithDetailsAsync(plan.Id, ct);
    }
}
