using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record UpdateSubscriptionPriceCommand(Guid SubscriptionId, decimal NewPrice);

public static class UpdateSubscriptionPriceHandler
{
    public static async Task<Result> Handle(
        UpdateSubscriptionPriceCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IPlanRepository planRepo,
        IUnitOfWork uow,
        ILogger<UpdateSubscriptionPriceCommand> logger,
        CancellationToken ct)
    {
        if (cmd.NewPrice < 0)
            return Result.Failure(new Error("Subscription.InvalidPrice", "Price cannot be negative."));

        var subscription = await subscriptionRepo.GetByIdAsync(cmd.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", $"Subscription {cmd.SubscriptionId} not found."));

        var plan = await planRepo.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var priceResult = plan.UpdatePricing(cmd.NewPrice, cmd.NewPrice * 12, plan.PricePerAdditionalSeat);
        if (priceResult.IsFailure)
            return priceResult;

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Subscription price updated: {SubId}, NewPrice: {Price}", cmd.SubscriptionId, cmd.NewPrice);
        return Result.Success();
    }
}
