using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;
using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Commands;

public record CreatePlanCommand(
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
    public static async Task<PlanDto> Handle(
        CreatePlanCommand cmd,
        IPlanRepository repo,
        IPlanReadService readService,
        IUnitOfWork uow,
        ILogger<CreatePlanCommand> logger,
        CancellationToken ct)
    {
        if (await repo.ExistsWithNameAsync(cmd.Name, ct))
            throw new InvalidOperationException($"Plan name '{cmd.Name}' already exists.");

        if (cmd.BasePriceMonthly < 0)
            throw new ArgumentException("Monthly price cannot be negative.");
        if (cmd.BasePriceAnnual > 0 && cmd.BasePriceAnnual > cmd.BasePriceMonthly * 12)
            throw new ArgumentException("Annual price cannot be greater than monthly price * 12.");
        if (cmd.IncludedSeats < 0)
            throw new ArgumentException("Included seats cannot be negative.");

        var features = cmd.Features
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (features.Count == 0)
            throw new ArgumentException("At least one feature is required.");

        var result = Plan.Create(
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

        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);

        var plan = result.Value;
        await repo.AddAsync(plan, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Plan created: {PlanId} ({Name})", plan.Id, plan.Name);
        return await readService.GetByIdWithDetailsAsync(plan.Id, ct);
    }
}
