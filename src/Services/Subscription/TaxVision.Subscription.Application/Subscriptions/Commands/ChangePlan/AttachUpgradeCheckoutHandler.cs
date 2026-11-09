using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary>Paso 3 de SUB-7: registra la sesión que PaymentApp creó para el upgrade.</summary>
public sealed record AttachUpgradeCheckoutCommand(
    Guid TenantId,
    Guid PlanChangeRequestId,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAtUtc,
    Guid ActorUserId
);

public static class AttachUpgradeCheckoutHandler
{
    public static async Task<Result> Handle(
        AttachUpgradeCheckoutCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var attached = subscription.AttachUpgradeCheckout(
            command.PlanChangeRequestId,
            command.PaymentId,
            command.CheckoutUrl,
            command.ExpiresAtUtc,
            command.ActorUserId,
            DateTime.UtcNow
        );
        if (attached.IsFailure)
        {
            // Se canceló mientras PaymentApp creaba la sesión: no puede quedar pagable.
            await bus.PublishAsync(new CancelHostedCheckoutCommand(command.TenantId, command.PaymentId));
            return attached;
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
