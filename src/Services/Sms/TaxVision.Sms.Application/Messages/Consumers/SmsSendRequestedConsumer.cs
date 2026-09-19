using BuildingBlocks.Messaging.SmsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Sms.Application.Messages.Commands;
using Wolverine;

namespace TaxVision.Sms.Application.Messages.Consumers;

/// <summary>
/// Punto de entrada genérico de envío de UN SMS: consume <see cref="SmsSendRequestedIntegrationEvent"/>
/// (que cualquier servicio publica: OTP de firma, invitación, entrega de documento/certificado,
/// reminders…) y lo ejecuta reusando <see cref="SendSmsBatchCommand"/> — con opt-out, idempotencia,
/// failover y proveedor ya resueltos allí. Espejo de <see cref="CampaignSmsDispatchConsumer"/>, pero
/// más directo: el evento ya trae el <c>CustomerId</c> (Guid) y la <c>IdempotencyKey</c>, así que no
/// hay que derivarlos. No cascada un resultado: <c>SendSmsBatchHandler</c> ya publica los eventos
/// agnósticos <c>SmsMessage{Accepted,Delivered,Failed,Suppressed}</c> correlacionados por
/// <see cref="SmsSendRequestedIntegrationEvent.SourceContext"/>/<c>CorrelationId</c>.
/// </summary>
public static class SmsSendRequestedConsumer
{
    public static async Task Handle(SmsSendRequestedIntegrationEvent evt, IMessageBus bus, CancellationToken ct)
    {
        await bus.InvokeAsync<Result<SendSmsBatchResponse>>(
            new SendSmsBatchCommand(
                evt.TenantId,
                evt.CorrelationId,
                [
                    new SmsSendItemDto(
                        evt.CustomerId,
                        evt.To,
                        evt.Body,
                        Media: null,
                        IdempotencyKey: evt.IdempotencyKey,
                        SourceContext: evt.SourceContext
                    ),
                ]
            ),
            ct
        );
    }
}
