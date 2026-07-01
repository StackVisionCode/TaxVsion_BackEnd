using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands;

// TenantId proviene del controller (extraído del JWT claim tenant_id)
public sealed record CancelAtPeriodEndCommand(Guid TenantId);

public static class CancelAtPeriodEndHandler
{
    public static async Task<Result> Handle(
        CancelAtPeriodEndCommand cmd,
        ISubscriptionRepository repo,
        IUnitOfWork uow,
        CancellationToken ct)
    {
        var subscription = await repo.GetActiveByTenantIdAsync(cmd.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "No active subscription found."));

        var result = subscription.CancelAtPeriodEnd();
        if (result.IsFailure)
            return result;

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
