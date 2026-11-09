using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;
using Wolverine.Attributes;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;

/// <summary>
/// Reembolso de plataforma (patrón BB-8, PAY-3). Sin transacción abierta: guarda la intención, llama al proveedor con la
/// clave de esa intención y registra el resultado en otra transacción. La misma petición (por cabecera, o por monto,
/// motivo y admin dentro de <see cref="SaaSPaymentRefundOptions.DuplicateRequestWindow"/>) nunca reembolsa dos veces.
/// </summary>
public static class RefundSaaSPaymentHandler
{
    [NonTransactional]
    public static async Task<Result> Handle(
        RefundSaaSPaymentCommand command,
        IMessageBus bus,
        IPaymentAdapterFactory providerFactory,
        IPaymentAttemptThrottle throttle,
        IOptions<SaaSPaymentRefundOptions> options,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var hasClientKey = !string.IsNullOrWhiteSpace(command.ClientIdempotencyKey);
        var prepared = await bus.InvokeAsync<Result<SaaSPaymentRefundPlan>>(
            new PrepareSaaSPaymentRefundCommand(
                command.SaaSPaymentId,
                command.RefundAmountCents,
                command.Reason,
                command.ActorUserId,
                RequestKey(command),
                hasClientKey ? $"refund:{command.SaaSPaymentId:N}:{command.ClientIdempotencyKey!.Trim()}" : null,
                hasClientKey ? null : options.Value.DuplicateRequestWindow,
                EnforceAdminThrottle: true
            ),
            ct
        );
        if (prepared.IsFailure)
            return prepared;

        var plan = prepared.Value;
        switch (plan.Kind)
        {
            case SaaSPaymentRefundPlanKind.AlreadyCompleted:
                logger.LogInformation(
                    "Refund request {RefundRequestId} of SaaSPayment {SaaSPaymentId} was already recorded; the provider is not called again.",
                    plan.RequestId,
                    command.SaaSPaymentId
                );
                return Result.Success();
            case SaaSPaymentRefundPlanKind.NeedsReconciliation:
                return NeedsReconciliation();
        }

        var adapter = providerFactory.Resolve(plan.Provider);
        var refund = await adapter.RefundAsync(plan.ChargeReference, plan.Amount, plan.Reason, plan.ProviderKey, ct);

        RecordSaaSPaymentRefundCommand record;
        if (refund.IsSuccess)
            record = new(command.SaaSPaymentId, plan.RequestId, refund.Value.ProviderRefundReference);
        else if (!ProviderRefundFailures.IsTransient(refund.Error))
            record = new(command.SaaSPaymentId, plan.RequestId, null, RejectionCode: refund.Error.Code);
        else if (adapter.Capabilities.SupportsIdempotencyKeys)
        {
            // La intención sigue abierta: la misma petición repite la misma clave.
            logger.LogWarning(
                "Provider refund for SaaSPayment {SaaSPaymentId} is temporarily unavailable: {Error}. Retry the same request.",
                command.SaaSPaymentId,
                refund.Error.Message
            );
            return Result.Failure(refund.Error);
        }
        else
            record = new(command.SaaSPaymentId, plan.RequestId, null, Unrecorded: true);

        var recorded = await bus.InvokeAsync<Result<SaaSPaymentRefundRecord>>(record, ct);
        if (recorded.IsFailure)
            return recorded;

        switch (recorded.Value.Kind)
        {
            case SaaSPaymentRefundRecordKind.Recorded:
                await throttle.RegisterAdminActionAttemptAsync(plan.TenantId, ct);
                return Result.Success();
            case SaaSPaymentRefundRecordKind.Rejected:
                return Result.Failure(refund.Error);
            default:
                return NeedsReconciliation();
        }
    }

    // La cabecera identifica la petición; sin ella, la huella (monto, admin y motivo).
    private static string RequestKey(RefundSaaSPaymentCommand command)
    {
        if (!string.IsNullOrWhiteSpace(command.ClientIdempotencyKey))
            return $"header:{command.ClientIdempotencyKey.Trim()}";

        var reason = SHA256.HashData(Encoding.UTF8.GetBytes(command.Reason?.Trim() ?? string.Empty));
        return $"auto:{command.RefundAmountCents}:{command.ActorUserId:N}:{Convert.ToHexString(reason)[..16]}";
    }

    private static Result NeedsReconciliation() =>
        Result.Failure(
            new Error(
                "SaaSPayment.RefundNeedsReconciliation",
                "The provider may have refunded this request but it is not recorded. It will not be sent again; check the payment in the review list."
            )
        );
}
