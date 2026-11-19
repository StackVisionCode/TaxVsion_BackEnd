using BuildingBlocks.Common;
using BuildingBlocks.Messaging.WalletIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ExecuteSaaSPaymentCharge;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Consume el intent de recarga de monedero publicado por <c>TaxVision.Wallet</c> y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. La recarga automática cobra off-session solo el método que la oficina
/// autorizó en Wallet (su consentimiento propio, no el mandato de la suscripción). Mismo
/// mecanismo que <see cref="SubscriptionPlanChangeDueConsumer"/>:
/// <see cref="ChargeSaaSPaymentCommand.TargetAggregateId"/> = <c>TopUpId</c>, para que
/// el aviso de resultado (<c>SaaSPaymentDomainEventHandler</c>) lo devuelva en el round-trip.
/// </summary>
public static class WalletTopUpDueConsumer
{
    public static async Task Handle(
        WalletTopUpDueIntegrationEvent evt,
        IMessageBus bus,
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
            var (provider, authorizedMethod) = TargetOf(evt);
            var command = new ChargeSaaSPaymentCommand(
                TenantId: evt.TenantId,
                IdempotencyKey: evt.IdempotencyKey,
                AmountCents: evt.AmountCents,
                Currency: evt.Currency,
                Type: SaaSPaymentType.WalletTopUp,
                TargetAggregateId: evt.TopUpId,
                Provider: provider,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: evt.RequestedByUserId,
                Strategy: evt.Automatic ? SaaSPaymentChargeStrategy.OffSession : SaaSPaymentChargeStrategy.Authorize,
                AuthorizedMethodId: authorizedMethod
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for wallet top-up {TopUpId}: {ErrorCode} — {ErrorMessage}",
                    evt.TopUpId,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }

    /// <summary>Proveedor y método a cobrar. La automática exige el método autorizado en Wallet (vacío = el pago falla
    /// con MethodUnavailable); un proveedor desconocido nunca se cambia por otro.</summary>
    private static (PaymentProviderCode Provider, string? AuthorizedMethod) TargetOf(WalletTopUpDueIntegrationEvent evt)
    {
        if (string.IsNullOrWhiteSpace(evt.Provider))
            return (PaymentProviderCode.Stripe, evt.Automatic ? string.Empty : evt.PaymentMethodReference);

        if (!TryParseProvider(evt.Provider, out var provider))
            return (PaymentProviderCode.Stripe, string.Empty);

        return (provider, evt.Automatic ? evt.PaymentMethodReference ?? string.Empty : evt.PaymentMethodReference);
    }

    private static bool TryParseProvider(string value, out PaymentProviderCode provider) =>
        Enum.TryParse(value.Trim(), ignoreCase: true, out provider)
        && Enum.IsDefined(provider)
        && !int.TryParse(value, out _);
}
