using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ChargeSaaSPayment;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using Wolverine;

namespace TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;

/// <summary>
/// Consume el intent de renovación de un add-on y lo traduce a un
/// <see cref="ChargeSaaSPaymentCommand"/>. Independiente de la suscripción base y de los
/// seats. El provider sale del <c>TenantBillingPreference</c> del tenant vía
/// <see cref="DunningProviderResolver"/>; antes se hardcodeaba Stripe y los addons de un tenant
/// con PayPal fallaban eternamente.
/// </summary>
public static class AddOnRenewalDueConsumer
{
    public static async Task Handle(
        AddOnRenewalDueIntegrationEvent evt,
        IMessageBus bus,
        ITenantBillingPreferenceProjectionRepository preferences,
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
            var preference = await preferences.GetAsync(evt.TenantId, ct);
            var provider = DunningProviderResolver.Resolve(preference);
            var command = new ChargeSaaSPaymentCommand(
                TenantId: evt.TenantId,
                IdempotencyKey: evt.IdempotencyKey,
                AmountCents: evt.AmountCents,
                Currency: evt.Currency,
                Type: SaaSPaymentType.AddOnRenewal,
                TargetAggregateId: evt.TenantAddOnId,
                Provider: provider,
                PayerEmail: SyntheticPayer.EmailFor(evt.TenantId),
                PayerName: null,
                RequestedByUserId: Guid.Empty
            );

            var result = await bus.InvokeAsync<Result<Guid>>(command, ct);
            if (result.IsFailure)
            {
                logger.LogError(
                    "ChargeSaaSPayment failed for renewal of add-on {TenantAddOnId} (provider {Provider}): {ErrorCode} — {ErrorMessage}",
                    evt.TenantAddOnId,
                    provider,
                    result.Error.Code,
                    result.Error.Message
                );
            }
        }
    }
}
