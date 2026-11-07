using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Platform;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Rama el intent de renovación según la estrategia (<see cref="ChargeStrategySelector"/>): off-session guarda el pago
/// en <c>Pending</c> y deja el <see cref="ExecuteSaaSPaymentChargeCommand"/> que cobra con <see cref="IOffSessionCharger"/>
/// en la misma transacción (patrón BB-8); si no, cae al flow legacy (<see cref="ChargeSaaSPaymentCommand"/>) para que el
/// fallo con requires_payment_method mande al tenant al banner "Pay now".
/// </summary>
public static class SubscriptionRenewalDueConsumer
{
    public static async Task Handle(
        SubscriptionRenewalDueIntegrationEvent evt,
        IMessageBus bus,
        ITenantBillingPreferenceProjectionRepository preferences,
        ITenantProviderCustomerRepository customers,
        IOffSessionChargerFactory offSessionFactory,
        ISaaSPaymentRepository payments,
        IPlatformSettings platform,
        IPaymentAppMetrics metrics,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<SaaSPayment> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            // Idempotencia: Wolverine puede reentregar el evento. Si ya hay un SaaSPayment con la
            // misma IdempotencyKey, no se vuelve a cobrar — un retry ve el mismo pago.
            var existing = await payments.GetByIdempotencyKeyAsync(evt.IdempotencyKey, ct);
            if (existing is not null)
            {
                logger.LogInformation(
                    "SaaSPayment already exists for renewal {IdempotencyKey}; skipping (idempotent).",
                    evt.IdempotencyKey
                );
                return;
            }

            var preference = await preferences.GetAsync(evt.TenantId, ct);
            var strategy = await ChargeStrategySelector.SelectAsync(
                evt.TenantId,
                preference,
                customers,
                offSessionFactory,
                ct
            );

            if (strategy is { ProviderCode: { } provider, PaymentMethodToken: { } paymentMethodToken })
            {
                await HandleOffSessionAsync(
                    evt,
                    provider,
                    paymentMethodToken,
                    payments,
                    platform,
                    metrics,
                    bus,
                    unitOfWork,
                    correlationId,
                    logger,
                    ct
                );
                return;
            }

            logger.LogInformation(
                "Off-session renewal not possible for tenant {TenantId}: {Reason}. Falling back to legacy charge flow.",
                evt.TenantId,
                strategy.FallbackReason
            );

            await DispatchLegacyChargeAsync(evt, DunningProviderResolver.Resolve(preference), bus, logger, ct);
        }
    }

    private static async Task HandleOffSessionAsync(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider,
        string paymentMethodToken,
        ISaaSPaymentRepository payments,
        IPlatformSettings platform,
        IPaymentAppMetrics metrics,
        IMessageBus bus,
        IUnitOfWork unitOfWork,
        string correlationId,
        ILogger logger,
        CancellationToken ct
    )
    {
        var paymentResult = SubscriptionRenewalCharge.BuildPayment(evt, provider, platform);
        if (paymentResult.IsFailure)
        {
            logger.LogError(
                "Could not create SaaSPayment for off-session renewal: {Error}",
                paymentResult.Error.Message
            );
            await DispatchLegacyChargeAsync(evt, provider, bus, logger, ct);
            return;
        }
        var payment = paymentResult.Value;
        await payments.AddAsync(payment, ct);
        metrics.RecordAttempted(provider.ToString(), payment.Type.ToString());

        // Guardar antes de publicar: fila y comando salen juntos o no sale ninguno.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ExecuteSaaSPaymentChargeCommand(
                evt.TenantId,
                payment.Id,
                SaaSPaymentChargeStrategy.OffSession,
                paymentMethodToken,
                CustomerReference: null,
                ActorUserId: Guid.Empty,
                correlationId
            )
        );

        logger.LogInformation(
            "Off-session renewal for tenant {TenantId} queued as SaaSPayment {SaaSPaymentId} (provider {Provider}).",
            evt.TenantId,
            payment.Id,
            provider
        );
    }

    private static async Task DispatchLegacyChargeAsync(
        SubscriptionRenewalDueIntegrationEvent evt,
        PaymentProviderCode provider,
        IMessageBus bus,
        ILogger logger,
        CancellationToken ct
    )
    {
        var result = await bus.InvokeAsync<Result<Guid>>(SubscriptionRenewalCharge.BuildLegacyCommand(evt, provider), ct);
        if (result.IsFailure)
        {
            logger.LogError(
                "ChargeSaaSPayment (legacy fallback) failed for renewal of subscription {TenantSubscriptionId} (provider {Provider}): {Code} — {Msg}",
                evt.TenantSubscriptionId,
                provider,
                result.Error.Code,
                result.Error.Message
            );
        }
    }
}
