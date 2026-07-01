using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public record UpdateSubscriptionPriceCommand(Guid SubscriptionId, decimal NewPrice);

public static class UpdateSubscriptionPriceHandler
{
    public static async Task<bool> Handle(
        UpdateSubscriptionPriceCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IPlanRepository planRepo,
        IUnitOfWork uow,
        ILogger<UpdateSubscriptionPriceCommand> logger,
        CancellationToken ct)
    {
        if (cmd.NewPrice < 0)
            throw new ArgumentException("Price cannot be negative.");

        var subscription = await subscriptionRepo.GetByIdAsync(cmd.SubscriptionId, ct)
            ?? throw new InvalidOperationException($"Subscription {cmd.SubscriptionId} not found.");

        var plan = await planRepo.GetByIdAsync(subscription.PlanId, ct)
            ?? throw new InvalidOperationException("Plan not found.");

        var priceResult = plan.UpdatePricing(cmd.NewPrice, cmd.NewPrice * 12, plan.PricePerAdditionalSeat);
        if (priceResult.IsFailure)
            throw new InvalidOperationException(priceResult.Error.Message);

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Subscription price updated: {SubId}, NewPrice: {Price}", cmd.SubscriptionId, cmd.NewPrice);
        return true;
    }
}
