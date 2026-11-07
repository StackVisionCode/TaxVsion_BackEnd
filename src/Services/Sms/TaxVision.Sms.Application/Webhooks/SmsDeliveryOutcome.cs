using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Providers;
using TaxVision.Sms.Domain.Messages;
using TaxVision.Sms.Domain.Webhooks;
using Wolverine;

namespace TaxVision.Sms.Application.Webhooks;

/// <summary>Aplicar un estado del proveedor y avisarlo: igual por webhook, por conciliación y al aceptar el envío.</summary>
internal static class SmsDeliveryOutcome
{
    /// <summary>Un estado que no aplica (mensaje ya avanzado) no cambia nada: el dominio lo rechaza.</summary>
    public static void Apply(SmsMessage message, SmsDeliveryUpdate update, DateTime nowUtc) =>
        _ = update.Status switch
        {
            SmsCanonicalStatus.Delivered => message.MarkDelivered(nowUtc),
            SmsCanonicalStatus.Failed => message.MarkFailed(nowUtc, update.FailureCode, update.FailureReason),
            SmsCanonicalStatus.Undeliverable => message.MarkUndeliverable(
                nowUtc,
                update.FailureCode,
                update.FailureReason
            ),
            SmsCanonicalStatus.Accepted => message.MarkAccepted(
                message.ProviderMessageId ?? update.ProviderMessageId,
                nowUtc
            ),
            _ => Result.Success(),
        };

    public static SmsDeliveryUpdate ToUpdate(PendingDeliveryReceipt receipt) =>
        new(
            receipt.ProviderMessageId,
            receipt.EventType,
            Enum.Parse<SmsCanonicalStatus>(receipt.CanonicalStatus),
            receipt.FailureCode,
            receipt.FailureReason
        );

    /// <summary>Avisa el estado actual del mensaje. Se llama después de guardar y solo si cambió.</summary>
    public static async Task PublishAsync(IMessageBus bus, SmsMessage message)
    {
        bus.TenantId = message.TenantId.ToString();
        if (message.Status == SmsMessageStatus.Accepted)
            await PublishAcceptedAsync(bus, message);
        else if (message.Status == SmsMessageStatus.Delivered)
            await bus.PublishAsync(
                new SmsMessageDeliveredIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                }
            );
        else if (message.Status is SmsMessageStatus.Failed or SmsMessageStatus.Undeliverable)
            await bus.PublishAsync(
                new SmsMessageFailedIntegrationEvent
                {
                    TenantId = message.TenantId,
                    CorrelationId = message.CorrelationId,
                    MessageId = message.Id,
                    CustomerId = message.CustomerId,
                    SourceContext = message.SourceContext,
                    ProviderMessageId = message.ProviderMessageId,
                    FailureCode = message.FailureCode,
                }
            );
    }

    public static async Task PublishAcceptedAsync(IMessageBus bus, SmsMessage message) =>
        await bus.PublishAsync(
            new SmsMessageAcceptedIntegrationEvent
            {
                TenantId = message.TenantId,
                CorrelationId = message.CorrelationId,
                MessageId = message.Id,
                CustomerId = message.CustomerId,
                SourceContext = message.SourceContext,
                ProviderMessageId = message.ProviderMessageId,
            }
        );
}
