using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;

public static class ChargeSaaSPaymentHandler
{
    /// <summary>Descriptor por defecto para todo cobro SaaS en Fase A. Personalización por
    /// tenant/plan queda para una fase futura (PaymentAppSettings).</summary>
    private const string DefaultStatementDescriptor = "TAXVISION SAAS";

    public static async Task<Result<Guid>> Handle(
        ChargeSaaSPaymentCommand command,
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
        var existing = await payments.GetByIdempotencyKeyAsync(command.IdempotencyKey, ct);
        if (existing is not null)
        {
            logger.LogInformation(
                "SaaSPayment already exists for IdempotencyKey {Key}; skipping (idempotent).",
                command.IdempotencyKey
            );
            return Result.Success(existing.Id);
        }

        var preparedResult = PrepareNewPayment(command);
        if (preparedResult.IsFailure)
            return Result.Failure<Guid>(preparedResult.Error);

        var payment = preparedResult.Value;
        await payments.AddAsync(payment, ct);

        metrics.RecordAttempted(command.Provider.ToString(), command.Type.ToString());

        var adapter = providerFactory.Resolve(command.Provider);
        await ExecuteChargeAsync(payment, adapter, providerCustomers, command, metrics, ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            payment.TenantId,
            nameof(SaaSPayment),
            payment.Id,
            SaaSPaymentChargeOutcome.MapAuditAction(payment.Status),
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: (object?)null,
            after: new
            {
                payment.Status,
                payment.FailureCode,
                payment.FailureReason,
            },
            reason: null,
            DateTime.UtcNow,
            ct
        );

        await SaaSPaymentChargeOutcome.PublishResultAsync(payment, bus, correlation, ct);

        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} for tenant {TenantId} finished with status {Status}.",
            payment.Id,
            payment.TenantId,
            payment.Status
        );

        return Result.Success(payment.Id);
    }

    private static Result<SaaSPayment> PrepareNewPayment(ChargeSaaSPaymentCommand command)
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(DefaultStatementDescriptor);
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment.Create(
            command.TenantId,
            keyResult.Value,
            amountResult.Value,
            command.Type,
            command.TargetAggregateId,
            command.Provider,
            descriptorResult.Value,
            command.RequestedByUserId,
            DateTime.UtcNow,
            command.CodeReservationId,
            command.CodeReservationPaymentId,
            command.DiscountAmountCents,
            command.PromotionSnapshotHash
        );
    }

    private static async Task ExecuteChargeAsync(
        SaaSPayment payment,
        IPaymentProvider adapter,
        ITenantProviderCustomerRepository providerCustomers,
        ChargeSaaSPaymentCommand command,
        IPaymentAppMetrics metrics,
        CancellationToken ct
    )
    {
        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow);

        var payerResult = await SaaSPaymentChargeOutcome.ResolvePayerAsync(
            command.TenantId,
            command.PayerEmail,
            command.PayerName,
            providerCustomers,
            adapter,
            ct
        );
        if (payerResult.IsFailure)
        {
            SaaSPaymentChargeOutcome.FailPayment(
                payment,
                payerResult.Error,
                command.RequestedByUserId,
                nextRetryAtUtc,
                metrics
            );
            return;
        }

        var chargeRequest = new ChargeAuthorizationRequest(
            Customer: payerResult.Value.Customer,
            Amount: payment.Amount,
            IdempotencyKey: payment.IdempotencyKey,
            Descriptor: payment.StatementDescriptor,
            Metadata: new Dictionary<string, string>
            {
                ["tenantId"] = command.TenantId.ToString("N"),
                ["saaSPaymentId"] = payment.Id.ToString("N"),
            },
            SpecificPaymentMethod: payerResult.Value.Method
        );

        var chargeResult = await adapter.AuthorizeChargeAsync(chargeRequest, ct);
        if (chargeResult.IsFailure)
        {
            SaaSPaymentChargeOutcome.FailPayment(
                payment,
                chargeResult.Error,
                command.RequestedByUserId,
                nextRetryAtUtc,
                metrics
            );
            return;
        }

        SaaSPaymentChargeOutcome.ApplyChargeOutcome(
            payment,
            chargeResult.Value,
            command.RequestedByUserId,
            nextRetryAtUtc,
            metrics
        );
    }
}
