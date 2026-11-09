using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RetrySaaSPayment;

/// <summary>Reabre el pago y deja el comando que llama al provider en la misma transacción (patrón BB-8).</summary>
public static class RetrySaaSPaymentHandler
{
    public static async Task<Result> Handle(
        RetrySaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        ITenantProviderCustomerRepository providerCustomers,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var payment = await payments.GetByIdAsync(command.SaaSPaymentId, command.TenantId, ct);
        if (payment is null)
            return Result.Failure(new Error("SaaSPayment.NotFound", "SaaSPayment does not exist."));

        var nowUtc = DateTime.UtcNow;
        var prepareResult = payment.PrepareForRetry(command.ActorUserId, nowUtc);
        if (prepareResult.IsFailure)
            return prepareResult;

        metrics.RecordAttempted(payment.ProviderCode.ToString(), payment.Type.ToString());

        var adapter = providerFactory.Resolve(payment.ProviderCode);
        var payerResult = await SaaSPaymentChargeOutcome.ResolvePayerAsync(
            payment.TenantId,
            providerCustomers,
            adapter,
            ct
        );
        if (payerResult.IsSuccess)
        {
            // Guardar antes de publicar: fila y comando salen juntos o no sale ninguno.
            await unitOfWork.SaveChangesAsync(ct);
            await bus.PublishAsync(
                new ExecuteSaaSPaymentChargeCommand(
                    payment.TenantId,
                    payment.Id,
                    SaaSPaymentChargeStrategy.Authorize,
                    payerResult.Value.Method.Token,
                    payerResult.Value.Customer.Token,
                    command.ActorUserId,
                    correlation.CorrelationId
                )
            );
            return Result.Success();
        }

        SaaSPaymentChargeOutcome.FailPayment(
            payment,
            payerResult.Error,
            command.ActorUserId,
            DunningPolicy.Decide(payment, DateTime.UtcNow),
            metrics
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            SaaSPaymentChargeOutcome.MapAuditAction(payment.Status),
            command.ActorUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                payment.FailureCode,
                payment.FailureReason,
                RetryAttempt = payment.Attempts.Count,
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        // El resultado lo avisa SaaSPaymentDomainEventHandler con el commit (evento de dominio).
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} retry finished with status {Status} (attempt {Attempt}).",
            payment.Id,
            payment.Status,
            payment.Attempts.Count
        );

        return Result.Success();
    }
}
