using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.Common;

/// <summary>
/// SUB-7: pedido a PaymentApp de cerrar una sesión de checkout. Se publica en la transacción que confirma la
/// cancelación local (cola local durable), así el pedido sale siempre y el HTTP queda fuera de esa transacción.
/// </summary>
public sealed record CancelHostedCheckoutCommand(Guid TenantId, Guid SaaSPaymentId);

public static class CancelHostedCheckoutHandler
{
    [NonTransactional]
    public static async Task Handle(
        CancelHostedCheckoutCommand command,
        IHostedCheckoutCancellationClient cancellation,
        ILogger<CancelHostedCheckoutCommand> logger,
        CancellationToken ct
    )
    {
        var result = await cancellation.CancelAsync(command.TenantId, command.SaaSPaymentId, ct);
        if (result.IsFailure)
            logger.LogWarning(
                "Provider cancel for payment {PaymentId} returned failure: {Error}; local state is already cancelled.",
                command.SaaSPaymentId,
                result.Error.Code
            );
    }
}
