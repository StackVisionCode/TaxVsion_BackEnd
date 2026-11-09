using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary>
/// SUB-7: orquesta sin transacción. <see cref="RequestPlanChangeHandler"/> confirma el cambio pedido; si el upgrade
/// se cobra por checkout hosteado, la llamada a PaymentApp sale después de ese commit, sin bloquear la fila de la
/// suscripción, y la sesión se adjunta en otra transacción. El orden del cobro no cambia: primero el request.
/// </summary>
public static class ChangePlanHandler
{
    [NonTransactional]
    public static async Task<Result<ChangePlanResult>> Handle(
        ChangePlanCommand command,
        IMessageBus bus,
        IPlanChangeCheckoutPaymentClient checkoutClient,
        CancellationToken ct
    )
    {
        var requested = await bus.InvokeAsync<Result<PlanChangeRequested>>(new RequestPlanChangeCommand(command), ct);
        if (requested.IsFailure)
            return Result.Failure<ChangePlanResult>(requested.Error);

        if (requested.Value.HostedCheckout is not { } checkoutRequest)
            return Result.Success(requested.Value.Outcome);

        var checkout = await checkoutClient.CreateCheckoutAsync(checkoutRequest, ct);
        if (checkout.IsFailure)
        {
            // SUB-13: la URL no llegó a nadie, así que no hay pago en curso que esperar.
            await bus.InvokeAsync<Result>(
                new ReleaseUpgradeWithoutCheckoutCommand(
                    command.TenantId,
                    checkoutRequest.PlanChangeRequestId,
                    command.RequestedByUserId
                ),
                ct
            );
            return Result.Failure<ChangePlanResult>(checkout.Error);
        }

        var attached = await bus.InvokeAsync<Result>(
            new AttachUpgradeCheckoutCommand(
                command.TenantId,
                checkoutRequest.PlanChangeRequestId,
                checkout.Value.PaymentId,
                checkout.Value.CheckoutUrl,
                checkout.Value.ExpiresAtUtc,
                command.RequestedByUserId
            ),
            ct
        );
        if (attached.IsFailure)
            return Result.Failure<ChangePlanResult>(attached.Error);

        return Result.Success(requested.Value.Outcome with { CheckoutUrl = checkout.Value.CheckoutUrl });
    }
}
