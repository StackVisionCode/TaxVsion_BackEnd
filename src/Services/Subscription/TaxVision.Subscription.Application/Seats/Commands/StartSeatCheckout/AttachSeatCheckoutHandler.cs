using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.StartSeatCheckout;

/// <summary>Paso 3 de SUB-7: registra la sesión que PaymentApp creó.</summary>
public sealed record AttachSeatCheckoutCommand(
    Guid TenantId,
    Guid IntentId,
    Guid PaymentId,
    string CheckoutUrl,
    DateTime ExpiresAtUtc
);

public static class AttachSeatCheckoutHandler
{
    public static async Task<Result> Handle(
        AttachSeatCheckoutCommand command,
        ISeatPurchaseIntentRepository intents,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(command.IntentId, command.TenantId, ct);
        if (intent is null)
            return Result.Failure(new Error("SeatPurchaseIntent.NotFound", "Seat purchase intent does not exist."));

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
