using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

public record RenewSubscriptionCommand(Guid SubscriptionId, bool IsAutomatic, DateTime? NewEndDate = null);

public static class RenewSubscriptionHandler
{
    public static async Task<bool> Handle(
        RenewSubscriptionCommand cmd,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        ILogger<RenewSubscriptionCommand> logger,
        CancellationToken ct)
    {
        var subscription = await repo.GetByIdAsync(cmd.SubscriptionId, ct)
            ?? throw new InvalidOperationException($"Subscription {cmd.SubscriptionId} not found.");

        var result = subscription.Renew(cmd.NewEndDate);
        if (result.IsFailure)
            throw new InvalidOperationException(result.Error.Message);

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Subscription renewed: {SubId}, period ends {End}",
            cmd.SubscriptionId, subscription.PeriodEndUtc);

        return true;
    }
}
