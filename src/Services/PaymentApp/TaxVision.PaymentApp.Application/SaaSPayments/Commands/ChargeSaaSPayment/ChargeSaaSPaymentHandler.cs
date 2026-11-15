using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.Common;
using TaxVision.PaymentApp.Application.Common.Branding;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;

/// <summary>Paso 1 del cobro (patrón BB-8): guarda el pago en <c>Pending</c> y deja el comando que llama al provider
/// en la misma transacción. Sin tarjeta guardada no hay nada que mandar y el pago se cierra acá.</summary>
public static class ChargeSaaSPaymentHandler
{
    /// <summary>Descriptor por defecto para todo cobro SaaS en Fase A. Personalización por
    /// tenant/plan queda para una fase futura (PaymentAppSettings).</summary>
    public static async Task<Result<Guid>> Handle(
        ChargeSaaSPaymentCommand command,
        ISaaSPaymentRepository payments,
        ITenantProviderCustomerRepository providerCustomers,
        IPaymentAdapterFactory providerFactory,
        IPaymentAuditLogWriter audit,
        IUnitOfWork unitOfWork,
        IPaymentAppMetrics metrics,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        IPlatformSettings platform,
        IMessageBus bus,
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

        var preparedResult = PrepareNewPayment(command, platform.Current);
        if (preparedResult.IsFailure)
            return Result.Failure<Guid>(preparedResult.Error);

        var payment = preparedResult.Value;
        await payments.AddAsync(payment, ct);

        metrics.RecordAttempted(command.Provider.ToString(), command.Type.ToString());

        var adapter = providerFactory.Resolve(command.Provider);
        var payerResult = await SaaSPaymentChargeOutcome.ResolvePayerAsync(
            command.TenantId,
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
                    command.TenantId,
                    payment.Id,
                    SaaSPaymentChargeStrategy.Authorize,
                    payerResult.Value.Method.Token,
                    payerResult.Value.Customer.Token,
                    command.RequestedByUserId,
                    correlation.CorrelationId
                )
            );
            return Result.Success(payment.Id);
        }

        SaaSPaymentChargeOutcome.FailPayment(
            payment,
            payerResult.Error,
            command.RequestedByUserId,
            DunningPolicy.Decide(payment, DateTime.UtcNow),
            metrics
        );

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

        // El resultado lo avisa SaaSPaymentDomainEventHandler con el commit (evento de dominio).
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "SaaSPayment {SaaSPaymentId} for tenant {TenantId} finished with status {Status}.",
            payment.Id,
            payment.TenantId,
            payment.Status
        );

        return Result.Success(payment.Id);
    }

    private static Result<SaaSPayment> PrepareNewPayment(
        ChargeSaaSPaymentCommand command,
        PlatformSettingsSnapshot platform
    )
    {
        var keyResult = IdempotencyKey.Create(command.IdempotencyKey);
        if (keyResult.IsFailure)
            return Result.Failure<SaaSPayment>(keyResult.Error);

        var amountResult = Money.Create(command.AmountCents, command.Currency);
        if (amountResult.IsFailure)
            return Result.Failure<SaaSPayment>(amountResult.Error);

        var descriptorResult = StatementDescriptor.Create(PlatformStatementDescriptors.For(platform, "SAAS"));
        if (descriptorResult.IsFailure)
            return Result.Failure<SaaSPayment>(descriptorResult.Error);

        return SaaSPayment
            .Create(
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
                command.PromotionSnapshotHash,
                ChargeBreakdowns.FromRequest(command.Quantity, command.UnitAmountCents, amountResult.Value.AmountCents)
            )
            .WithConceptFor(command.Type, command.Quantity, command.ConceptDisplayName)
            .WithLines(command.Lines);
    }
}
