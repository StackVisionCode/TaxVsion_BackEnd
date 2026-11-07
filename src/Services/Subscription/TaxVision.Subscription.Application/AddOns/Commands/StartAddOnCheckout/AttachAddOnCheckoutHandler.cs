using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;

/// <summary>Paso 3 de SUB-7: registra la sesión que PaymentApp creó.</summary>
public sealed record AttachAddOnCheckoutCommand(
    Guid TenantId,
    Guid IntentId,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAtUtc
);

public static class AttachAddOnCheckoutHandler
{
    public static async Task<Result> Handle(
        AttachAddOnCheckoutCommand command,
        IAddOnPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(command.IntentId, command.TenantId, ct);
        if (intent is null)
            return Result.Failure(new Error("AddOnPurchaseIntent.NotFound", "Add-on purchase intent does not exist."));

        var attached = intent.AttachCheckout(
            command.PaymentId,
            command.CheckoutUrl,
            command.ExpiresAtUtc,
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
