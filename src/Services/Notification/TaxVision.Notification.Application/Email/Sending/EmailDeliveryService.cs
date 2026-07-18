using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Configurations;
using TaxVision.Notification.Domain.Emailing.Sending;
using Wolverine;

namespace TaxVision.Notification.Application.Email.Sending;

/// <summary>
/// Entrega efectiva de un correo saliente ya persistido y renderizado. Resuelve la configuración de
/// proveedor (tenant → global), envía (SMTP hoy), actualiza estado/tracking, publica el evento de
/// resultado y persiste todo en una sola transacción (outbox). Reutilizable por envíos y campañas.
/// </summary>
/// <remarks>
/// Implementación registrada cuando <c>Notification:UsePostmasterDispatch=false</c> — desde Hardening
/// Fase 21 (2026-07-18) ese es el valor de ROLLBACK explícito, no el default (el default pasó a ser
/// <c>true</c>). Ver <see cref="PostmasterEmailDeliveryService"/> (Hardening Fase 19, 2026-07-18) para
/// la implementación registrada por default, que transporta vía Postmaster en vez de
/// <see cref="ISmtpSendClient"/> directo. Esta clase sigue viva sin cambios como fallback operacional:
/// retirarla es trabajo futuro fuera del plan de hardening, condicionado a confianza operacional real
/// en un despliegue en producción (ver Fase 21 del plan).
/// </remarks>
public sealed class EmailDeliveryService(
    IOutboundEmailRepository repository,
    IEmailConfigurationResolver configResolver,
    ISmtpSendClient smtp,
    IMessageBus bus,
    ICorrelationContext correlation,
    IUnitOfWork unitOfWork
) : IEmailDeliveryService
{
    public async Task<Result> DeliverAsync(Guid messageId, CancellationToken ct = default)
    {
        var message = await repository.GetForDeliveryAsync(messageId, ct);
        if (message is null)
            return Result.Failure(new Error("EmailMessage.NotFound", "Outbound message not found."));

        // Idempotencia: si ya se envió/canceló o agotó reintentos, no hace nada.
        if (!message.CanDeliver())
            return Result.Success();

        var config = await configResolver.ResolveAsync(message.TenantId, ct);
        if (config is null)
            return await FailAsync(message, "No active email configuration for tenant or system.", ct);

        // Solo SMTP por ahora; los proveedores por API se entregan con sus adaptadores (fases futuras).
        if (config.ProviderType != EmailProviderType.Smtp || string.IsNullOrWhiteSpace(config.Host))
            return await FailAsync(message, $"Provider '{config.ProviderType}' is not supported for delivery yet.", ct);

        message.MarkSending();

        var toAddresses = string.Join(
            ",",
            message.Recipients.Where(r => r.Kind == EmailRecipientKind.To).Select(r => r.Address)
        );
        var email = new EmailMessage(toAddresses, message.Subject, message.HtmlBody, message.TextBody);
        var connection = new SmtpConnection(
            config.Host!,
            config.Port ?? 587,
            config.Username,
            config.Password,
            config.UseSsl,
            config.FromEmail,
            config.FromName
        );

        var sendResult = await smtp.SendAsync(connection, email, ct);
        if (sendResult.IsFailure)
            return await FailAsync(message, sendResult.Error.Message, ct);

        message.MarkSent(config.ProviderType.ToString(), config.ConfigurationId);
        await bus.PublishAsync(
            new EmailDeliverySucceededIntegrationEvent
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                CorrelationId = correlation.CorrelationId,
                ProviderType = config.ProviderType.ToString(),
                CampaignId = message.CampaignId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }

    private async Task<Result> FailAsync(OutboundEmailMessage message, string error, CancellationToken ct)
    {
        message.MarkFailed(error);
        await bus.PublishAsync(
            new EmailDeliveryFailedIntegrationEvent
            {
                MessageId = message.Id,
                TenantId = message.TenantId,
                CorrelationId = correlation.CorrelationId,
                Error = message.Error ?? error,
                CampaignId = message.CampaignId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
