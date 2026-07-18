using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Application.Consumers.Postmaster;

/// <summary>
/// Contraparte de <c>PostmasterCallbackConsumers.cs</c> para el path
/// <see cref="TaxVision.Notification.Application.Email.Sending.PostmasterEmailDeliveryService"/> (Hardening Fase 19, 2026-07-18) —
/// resuelve <c>evt.NotificationLogId</c> contra <see cref="IOutboundEmailRepository"/> en vez de
/// <c>INotificationLogQueryRepository</c>, porque este path nunca crea un <c>NotificationLog</c>: reusa
/// el mismo campo como id opaco de <c>OutboundEmailMessage</c> (ver el comentario de clase de
/// <see cref="TaxVision.Notification.Application.Email.Sending.PostmasterEmailDeliveryService"/> para el porqué completo). Los 5 tipos de
/// callback se suscriben igual que en <c>PostmasterCallbackConsumers.cs</c>; cuando el callback
/// pertenece al OTRO espacio de ids (un <c>NotificationLog</c> real) simplemente no encuentra el
/// mensaje acá y no hace nada — dedup natural entre los dos consumers sin necesidad de un discriminador
/// explícito en el evento.
/// </summary>
public static class PostmasterOutboundEmailSucceededConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliverySucceededIntegrationEvent evt,
        IOutboundEmailRepository outbound,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var message = await outbound.GetForDeliveryAsync(evt.NotificationLogId, ct);
            if (message is null)
                return;

            message.MarkSent("Postmaster", configurationId: null);
            await uow.SaveChangesAsync(ct);
            await publisher.PublishAsync(
                new EmailDeliverySucceededIntegrationEvent
                {
                    MessageId = message.Id,
                    TenantId = message.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    ProviderType = "Postmaster",
                    CampaignId = message.CampaignId,
                },
                ct
            );
        }
    }

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

public static class PostmasterOutboundEmailFailedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliveryFailedIntegrationEvent evt,
        IOutboundEmailRepository outbound,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var message = await outbound.GetForDeliveryAsync(evt.NotificationLogId, ct);
            if (message is null)
                return;

            await FailAsync(message, evt.Reason, publisher, uow, correlation, ct);
        }
    }

    internal static async Task FailAsync(
        OutboundEmailMessage message,
        string reason,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        message.MarkFailed(reason);
        await uow.SaveChangesAsync(ct);
        await publisher.PublishAsync(
            // message.Error (no `reason` crudo) por si MarkFailed truncó el mensaje a 1024 chars —
            // mismo criterio que el FailAsync privado de EmailDeliveryService (path SMTP).
            new EmailDeliveryFailedIntegrationEvent
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                CorrelationId = correlation.CorrelationId,
                Error = message.Error ?? reason,
                CampaignId = message.CampaignId,
            },
            ct
        );
    }

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

/// <summary>
/// A diferencia de <c>PostmasterCallbackConsumers.cs</c> (que trata un bounce como un simple
/// <c>MarkFailed</c> sobre <c>NotificationDispatchAttempt</c>), <see cref="OutboundEmailMessage"/> ya
/// tenía un estado <c>Bounced</c> propio (<c>MarkBounced</c>/<c>BouncedAtUtc</c>) que antes de esta fase
/// solo alimentaba <c>EmailWebhooksController</c> — un webhook nunca conectado a ningún proveedor real
/// (retirado en esta misma fase, ver Program.cs). Este es el primer productor REAL de bounces para
/// <see cref="OutboundEmailMessage"/>.
/// </summary>
public static class PostmasterOutboundEmailBouncedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliveryBouncedIntegrationEvent evt,
        IOutboundEmailRepository outbound,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var message = await outbound.GetForDeliveryAsync(evt.NotificationLogId, ct);
            if (message is null)
                return;

            message.MarkBounced($"{evt.BounceType}: {evt.Reason}");
            await uow.SaveChangesAsync(ct);
            // Un bounce cuenta como fallo a efectos de los contadores de campaña — no existe un
            // "EmailDeliveryBouncedIntegrationEvent" separado que CampaignDeliveryFailedConsumer
            // escuche, y semánticamente un rebote SÍ es un envío fallido para ese propósito.
            await publisher.PublishAsync(
                new EmailDeliveryFailedIntegrationEvent
                {
                    MessageId = message.Id,
                    TenantId = message.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    Error = $"Bounce ({evt.BounceType}): {evt.Reason}",
                    CampaignId = message.CampaignId,
                },
                ct
            );
        }
    }

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

public static class PostmasterOutboundEmailSuppressedConsumer
{
    public static async Task Handle(
        PostmasterEmailDeliverySuppressedIntegrationEvent evt,
        IOutboundEmailRepository outbound,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var message = await outbound.GetForDeliveryAsync(evt.NotificationLogId, ct);
            if (message is null)
                return;

            await PostmasterOutboundEmailFailedConsumer.FailAsync(
                message,
                $"Suppressed: {evt.SuppressionReason}",
                publisher,
                uow,
                correlation,
                ct
            );
        }
    }

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}

public static class PostmasterOutboundEmailProviderNotConfiguredConsumer
{
    private const string ReasonMessage =
        "Tenant email provider not configured — configure your outbound SMTP in Settings → Email.";

    public static async Task Handle(
        PostmasterEmailDeliveryProviderNotConfiguredIntegrationEvent evt,
        IOutboundEmailRepository outbound,
        IIntegrationEventPublisher publisher,
        IUnitOfWork uow,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelation(evt.CorrelationId, evt.EventId)))
        {
            var message = await outbound.GetForDeliveryAsync(evt.NotificationLogId, ct);
            if (message is null)
                return;

            await PostmasterOutboundEmailFailedConsumer.FailAsync(
                message,
                ReasonMessage,
                publisher,
                uow,
                correlation,
                ct
            );
        }
    }

    private static string ResolveCorrelation(string correlationId, Guid eventId) =>
        string.IsNullOrWhiteSpace(correlationId) ? eventId.ToString("N") : correlationId;
}
