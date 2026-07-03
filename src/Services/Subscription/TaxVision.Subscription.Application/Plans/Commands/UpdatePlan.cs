using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Commands;

public sealed record UpdatePlanCommand(
    Guid Id,
    string Name,
    string Title,
    string Description,
    decimal BasePriceMonthly,
    decimal BasePriceAnnual,
    decimal PricePerAdditionalSeat,
    int IncludedSeats,
    bool IsActive,
    ServiceLevel ServiceLevel,
    List<string> Features);

public static class UpdatePlanHandler
{
    public static async Task<Result<PlanDto>> Handle(
        UpdatePlanCommand cmd,
        IPlanRepository repo,
        IPlanReadService readService,
        IUnitOfWork uow,
        ILogger<UpdatePlanCommand> logger,
        CancellationToken ct)
    {
        var plan = await repo.GetByIdAsync(cmd.Id, ct);
        if (plan is null)
            return Result.Failure<PlanDto>(new Error("Plan.NotFound", $"Plan {cmd.Id} not found."));

        if (cmd.BasePriceMonthly < 0)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidPrice", "Monthly price cannot be negative."));

        if (cmd.BasePriceAnnual > 0 && cmd.BasePriceAnnual > cmd.BasePriceMonthly * 12)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidPrice", "Annual price cannot exceed monthly * 12."));

        if (cmd.IncludedSeats < 0)
            return Result.Failure<PlanDto>(new Error("Plan.InvalidSeats", "Included seats cannot be negative."));

        var updateResult = plan.Update(cmd.Name, cmd.Title, cmd.Description, cmd.IsActive, cmd.ServiceLevel);
        if (updateResult.IsFailure)
            return Result.Failure<PlanDto>(updateResult.Error);

        var pricingResult = plan.UpdatePricing(cmd.BasePriceMonthly, cmd.BasePriceAnnual, cmd.PricePerAdditionalSeat);
        if (pricingResult.IsFailure)
            return Result.Failure<PlanDto>(pricingResult.Error);

        plan.UpdateSeats(cmd.IncludedSeats);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Plan updated: {PlanId}", plan.Id);
        return await readService.GetByIdWithDetailsAsync(plan.Id, ct);
    }
}
