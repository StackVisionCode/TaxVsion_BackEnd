using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary>SUB-13: PaymentApp no abrió la sesión. Si esto no llega a correr, lo cierra el barrido de checkouts.</summary>
public sealed record ReleaseUpgradeWithoutCheckoutCommand(Guid TenantId, Guid PlanChangeRequestId, Guid ActorUserId);

public static class ReleaseUpgradeWithoutCheckoutHandler
{
    public static async Task<Result> Handle(
        ReleaseUpgradeWithoutCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var released = subscription.ExpireUpgradeWithoutCheckout(
            command.PlanChangeRequestId,
            command.ActorUserId,
            DateTime.UtcNow
        );
        if (released.IsFailure)
            return released;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
