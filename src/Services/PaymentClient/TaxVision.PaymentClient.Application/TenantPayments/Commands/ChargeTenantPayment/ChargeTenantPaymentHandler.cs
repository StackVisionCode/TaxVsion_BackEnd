using BuildingBlocks.Results;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.ExecuteTenantPaymentCharge;
using TaxVision.PaymentClient.Application.TenantPayments.Commands.PrepareTenantPayment;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentClient.Application.TenantPayments.Commands.ChargeTenantPayment;

/// <summary>
/// Orquesta un cobro sin transacción abierta durante la llamada al proveedor (patrón BB-8): la intención se confirma
/// sola (<see cref="PrepareTenantPaymentCommand"/>) y después se cobra (<see cref="ExecuteTenantPaymentChargeCommand"/>).
/// </summary>
public static class ChargeTenantPaymentHandler
{
    [NonTransactional]
    public static async Task<Result<Guid>> Handle(
        ChargeTenantPaymentCommand command,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var prepared = await bus.InvokeAsync<Result<PreparedTenantPayment>>(
            new PrepareTenantPaymentCommand(command),
            ct
        );
        if (prepared.IsFailure)
            return Result.Failure<Guid>(prepared.Error);

        var paymentId = prepared.Value.TenantPaymentId;
        if (!prepared.Value.ReadyToCharge)
            return Result.Success(paymentId);

        var charged = await bus.InvokeAsync<Result<TenantPaymentChargeView>>(
            new ExecuteTenantPaymentChargeCommand(command.TenantId, paymentId, command.ActorUserId),
            ct
        );

        // Resultado sin guardar: el pago existe y lo resuelve la reconciliación; su estado se lee por id.
        if (charged.IsFailure && charged.Error.Code != ExecuteTenantPaymentChargeHandler.OutcomeUnknownCode)
            return Result.Failure<Guid>(charged.Error);

        return Result.Success(paymentId);
    }
}
