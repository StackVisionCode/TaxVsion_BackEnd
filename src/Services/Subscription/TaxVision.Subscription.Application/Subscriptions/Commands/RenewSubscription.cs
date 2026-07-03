using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public sealed record RenewSubscriptionCommand(Guid SubscriptionId, bool IsAutomatic, DateTime? NewEndDate = null);

public static class RenewSubscriptionHandler
{
    public static async Task<Result> Handle(
        RenewSubscriptionCommand cmd,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        ILogger<RenewSubscriptionCommand> logger,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(cmd.SubscriptionId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", $"Subscription {cmd.SubscriptionId} not found."));

        var result = subscription.Renew(cmd.NewEndDate);
        if (result.IsFailure)
            return result;

        await uow.SaveChangesAsync(ct);
        logger.LogInformation("Subscription renewed: {SubId}, ends {End}", cmd.SubscriptionId, subscription.PeriodEndUtc);
        return Result.Success();
    }
}
