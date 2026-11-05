using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Sending;

namespace TaxVision.Postmaster.Application.Sending.Commands.DispatchSentMessage;

/// <summary>
/// Paso de envío de un correo ya preparado (PMS-01). Va por una cola local durable y se confirma junto con
/// la reserva y el SentMessage. Lleva el pedido original (el cuerpo no se guarda en SentMessages) y, en el
/// carril del buzón, la cuenta resuelta. Nunca credenciales SMTP: esas se resuelven al enviar.
/// </summary>
public sealed record DispatchSentMessageCommand(
    Guid SentMessageId,
    Guid TenantId,
    string IdempotencyKey,
    DeliveryLane Lane,
    NotificationsEmailSendRequestedIntegrationEvent Request,
    ResolvedMailbox? Mailbox
);
