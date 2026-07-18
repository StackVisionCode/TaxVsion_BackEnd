using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.RetrySaaSPayment;

public static class RetrySaaSPaymentHandler
{
    public static async Task<Result> Handle(
        RetrySaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        ITenantProviderCustomerRepository providerCustomers,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
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
        await ExecuteRetryAsync(payment, adapter, providerCustomers, command.ActorUserId, metrics, ct);

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

        await SaaSPaymentChargeOutcome.PublishResultAsync(payment, bus, correlation, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} retry finished with status {Status} (attempt {Attempt}).",
            payment.Id,
            payment.Status,
            payment.Attempts.Count
        );

        return Result.Success();
    }

    private static async Task ExecuteRetryAsync(
        SaaSPayment payment,
        IPaymentProvider adapter,
        ITenantProviderCustomerRepository providerCustomers,
        Guid actorUserId,
        IPaymentAppMetrics metrics,
        CancellationToken ct
    )
    {
        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow);

        var payerResult = await SaaSPaymentChargeOutcome.ResolvePayerAsync(
            payment.TenantId,
            SyntheticPayer.EmailFor(payment.TenantId),
            null,
            providerCustomers,
            adapter,
            ct
        );
        if (payerResult.IsFailure)
        {
            SaaSPaymentChargeOutcome.FailPayment(payment, payerResult.Error, actorUserId, nextRetryAtUtc, metrics);
            return;
        }

        var chargeRequest = new ChargeAuthorizationRequest(
            Customer: payerResult.Value.Customer,
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = payment.TenantId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
                ["retry"] = "true",
            },
            SpecificPaymentMethod: payerResult.Value.Method
        );

        var chargeResult = await adapter.AuthorizeChargeAsync(chargeRequest, ct);
        if (chargeResult.IsFailure)
        {
            SaaSPaymentChargeOutcome.FailPayment(payment, chargeResult.Error, actorUserId, nextRetryAtUtc, metrics);
            return;
        }

        SaaSPaymentChargeOutcome.ApplyChargeOutcome(payment, chargeResult.Value, actorUserId, nextRetryAtUtc, metrics);
    }
}
