using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Sending;

namespace TaxVision.Notification.Application.Email.Sending;

/// <summary>
/// Implementación event-based de <see cref="IEmailDeliveryService"/> — Hardening Fase 19 (2026-07-18).
/// En vez de resolver una <see cref="Domain.Emailing.Configurations.EmailProviderConfiguration"/> y
/// llamar <c>ISmtpSendClient</c> directo (lo que hace <see cref="EmailDeliveryService"/>, la
/// implementación que esta reemplaza bajo el feature flag), publica
/// <c>notifications.email_send_requested.v1</c> hacia Postmaster y deja el mensaje en
/// <see cref="EmailStatus.Sending"/> hasta que <c>PostmasterOutboundEmailCallbackConsumers</c> reciba
/// el callback de resultado y complete <see cref="OutboundEmailMessage.MarkSent"/>/<c>MarkFailed</c>/
/// <c>MarkBounced</c> — mismo patrón asíncrono de dos fases que <c>EventBasedEmailDispatchGateway</c>
/// ya usa para el path de Auth/Signature/Communication.
/// </summary>
/// <remarks>
/// <para>
/// <b>Por qué esta clase y no reusar <see cref="IEmailDispatchGateway"/> tal cual</b>: ese gateway crea
/// y es dueño de un <c>NotificationLog</c>/<c>NotificationDispatchAttempt</c> propios — un modelo de
/// tracking distinto al de <see cref="OutboundEmailMessage"/> (que ya tiene su propio ciclo de vida,
/// lo consultan <c>GET /notifications/email/messages/{id}</c> y los contadores de
/// <c>EmailCampaigns</c> vía <see cref="EmailDeliverySucceededIntegrationEvent"/>/
/// <see cref="EmailDeliveryFailedIntegrationEvent"/>). Enrutar por el gateway habría creado un
/// <c>NotificationLog</c> huérfano por cada envío y dejado el <see cref="OutboundEmailMessage"/> real
/// congelado en <c>Sending</c> para siempre (los callbacks de <c>PostmasterCallbackConsumers</c> solo
/// saben actualizar <c>NotificationLog</c>) — habría roto tanto el historial de envío
/// (<c>/messages/{id}</c>) como los contadores de campaña. En cambio, esta clase publica el MISMO
/// evento y reusa <see cref="NotificationsEmailSendRequestedIntegrationEvent.NotificationLogId"/> como
/// lo que su propio XML doc ya dice que es — "la clave opaca de correlación que Postmaster devuelve en
/// los callbacks" — pasando <c>OutboundEmailMessage.Id</c> en vez de un <c>NotificationLog.Id</c> real.
/// <c>PostmasterOutboundEmailCallbackConsumers</c> (namespace <c>Consumers.Postmaster</c>) resuelve ese
/// mismo campo contra <see cref="IOutboundEmailRepository"/> en vez de <c>INotificationLogQueryRepository</c>.
/// Los dos espacios de ids nunca chocan: son tablas distintas con Guids aleatorios independientes, y
/// cada consumer no-encuentra-nada silenciosamente cuando el callback pertenece al otro espacio (mismo
/// criterio defensivo que <c>PostmasterCallbackConsumers</c> ya usa para "log desconocido").
/// </para>
/// <para>
/// <b>Impacto en EmailCampaigns (fuera de alcance de este plan, investigado igual)</b>:
/// <c>EmailCampaignBatchConsumer</c> y <c>SendCampaignTestHandler</c> (vía
/// <c>SendTemplateEmailHandler</c>) crean un <see cref="OutboundEmailMessage"/> y publican
/// <see cref="EmailSendRequestedIntegrationEvent"/> exactamente igual que el envío individual — este
/// cambio de transporte los alcanza automáticamente, a propósito. Se investigó si eso era seguro antes
/// de proceder: (1) ambos paths de campaña ya eran 100% asíncronos ANTES de esta fase — el HTTP siempre
/// devolvía 202/el resultado del command sin esperar el envío real, así que no hay ninguna expectativa
/// de confirmación síncrona que este cambio pueda romper; (2) el fan-out por lotes de 100 ya publicaba
/// un <see cref="OutboundEmailMessage"/>+evento POR DESTINATARIO (nunca fue un blast SMTP único), que es
/// exactamente el modelo per-message que Postmaster espera — de hecho Postmaster gana un rate limiter
/// por tenant que el path directo por SMTP nunca tuvo para campañas; (3) los contadores de campaña
/// (<c>CampaignDeliverySucceededConsumer</c>/<c>CampaignDeliveryFailedConsumer</c>) siguen
/// alimentándose de los mismos dos eventos de integración, ahora publicados por
/// <c>PostmasterOutboundEmailCallbackConsumers</c> en vez de por esta clase directamente — el contrato
/// que EmailCampaigns consume no cambió, solo quién lo produce y cuándo (tras el callback real de
/// Postmaster en vez de tras el SendAsync síncrono). Ningún hallazgo bloqueante — se procedió.
/// </para>
/// <para>
/// <b>Alcance (múltiples destinatarios "To")</b>: <c>notifications.email_send_requested.v1</c> tiene un
/// único campo <c>To</c> (diseño heredado de las notificaciones transaccionales de un solo destinatario
/// que Fase 4 migró primero, compartido hoy por 6 consumers). <see cref="OutboundEmailMessage"/> sí
/// permite más de un destinatario "To" (lo usa el endpoint público genérico
/// <c>POST /notifications/email/send</c>; ni las campañas ni el envío por plantilla los usan hoy). Este
/// método manda el primer "To" como <c>To</c> y el resto como <c>Cc</c> adicional — el destinatario
/// igual recibe el correo, solo cambia el header en el que aparece. Extender el contrato del evento a
/// una lista nativa de "To" es un cambio de mayor alcance (toca los 6 consumers existentes y el armado
/// de MIME en Postmaster) — no es parte de "reenrutar el transporte", que es el alcance de esta fase.
/// </para>
/// <para>
/// <b>Adjuntos</b>: igual que <see cref="EmailDeliveryService"/> (el path que reemplaza), no se
/// resuelven bytes de <c>AttachmentFileIdsJson</c> — ninguna de las dos implementaciones lo hacía antes
/// de esta fase (<c>EmailMessage</c>/<c>ISmtpSendClient</c> no tiene parámetro de adjuntos); no es una
/// regresión introducida acá.
/// </para>
/// <para>
/// <b>Scope de proveedor</b>: pide <c>ProviderScope.Tenant</c> (nunca <c>System</c>) porque estos son
/// correos de negocio del tenant (envío ad-hoc del usuario o campaña propia), no notificaciones
/// transaccionales de la plataforma. A diferencia del <c>ResolveAsync</c> tenant→sistema que usa
/// <see cref="EmailDeliveryService"/> hoy, el resolver de Postmaster NUNCA cae a System para scope
/// Tenant (política anti-spoofing documentada en <c>ProviderResolver</c>) — si el tenant no configuró
/// su <c>TenantEmailProvider</c> en Postmaster, el callback es <c>ProviderNotConfigured</c> en vez de
/// enviarse silenciosamente "From: TaxVision". Es un endurecimiento intencional, no un bug: evita que
/// el correo de negocio de un tenant salga con la identidad del sistema. La migración operativa de las
/// <c>EmailProviderConfigurations</c> existentes de Notification hacia el <c>TenantEmailProvider</c> de
/// Postmaster es un prerrequisito de confianza operacional para la Fase 21 (flip del flag), no de esta
/// fase (que solo construye el camino, sin activarlo por default).
/// </para>
/// </remarks>
public sealed class PostmasterEmailDeliveryService(
    IOutboundEmailRepository repository,
    IIntegrationEventPublisher publisher,
    ICorrelationContext correlation,
    IUnitOfWork unitOfWork
) : IEmailDeliveryService
{
    public async Task<Result> DeliverAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await repository.GetForDeliveryAsync(messageId, ct);
        if (message is null)
            return Result.Failure(new Error("EmailMessage.NotFound", "Outbound message not found."));

        // Idempotencia: si ya se envió/canceló, o el redelivery de Wolverine llega después de que el
        // mensaje ya quedó Sending/Sent por un intento anterior, no hace nada. La deduplicación fina
        // del lado de Postmaster corre por IdempotencyKey (ver más abajo).
        if (!message.CanDeliver())
            return Result.Success();

        message.MarkSending();

        var (to, cc, bcc) = SplitRecipients(message);
        var evt = new NotificationsEmailSendRequestedIntegrationEvent
        {
            TenantId = message.TenantId,
            CorrelationId = correlation.CorrelationId,
            NotificationLogId = message.Id,
            DispatchAttemptId = Guid.NewGuid(),
            IdempotencyKey = message.Id.ToString("N"),
            To = to,
            Subject = message.Subject,
            HtmlBody = message.HtmlBody,
            TextBody = message.TextBody,
            TemplateKey = message.TemplateId?.ToString() ?? DefaultTemplateKey(message),
            RequiredProviderScope = EmailDispatchScope.Tenant.ToString(),
            LogoScope = EmailDispatchScope.Tenant.ToString(),
            Stream = (
                message.CampaignId is null ? EmailDispatchStream.Transactional : EmailDispatchStream.Bulk
            ).ToString(),
            Cc = cc.Count == 0 ? null : cc,
            Bcc = bcc.Count == 0 ? null : bcc,
        };

        await publisher.PublishAsync(evt, ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private static (string To, List<string> Cc, List<string> Bcc) SplitRecipients(OutboundEmailMessage message)
    {
        var toAddresses = message
            .Recipients.Where(r => r.Kind == EmailRecipientKind.To)
            .Select(r => r.Address)
            .ToList();

        // OutboundEmailMessage.Create ya garantiza al menos un "To" — ver comentario de clase sobre por
        // qué el resto (si hay más de uno) se manda como Cc.
        var primaryTo = toAddresses[0];
        var extraTo = toAddresses.Skip(1);

        var cc = message
            .Recipients.Where(r => r.Kind == EmailRecipientKind.Cc)
            .Select(r => r.Address)
            .Concat(extraTo)
            .ToList();
        var bcc = message.Recipients.Where(r => r.Kind == EmailRecipientKind.Bcc).Select(r => r.Address).ToList();

        return (primaryTo, cc, bcc);
    }

    private static string DefaultTemplateKey(OutboundEmailMessage message) =>
        message.CampaignId is not null ? "notification.campaign_email" : "notification.direct_send";
}
