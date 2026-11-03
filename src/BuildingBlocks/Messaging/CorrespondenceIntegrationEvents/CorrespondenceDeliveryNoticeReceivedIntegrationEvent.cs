using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CorrespondenceIntegrationEvents;

/// <summary>
/// Un correo enviado desde el CRM no se entregó (o se demoró) y el aviso quedó en su conversación. Solo
/// ids y la categoría: el CRM lo refresca en vivo y la campana avisa a quien lo envió.
/// <see cref="CustomerId"/> = null si el destinatario no es cliente.
/// </summary>
[MessageIdentity("correspondence.delivery_notice_received.v1")]
public sealed record CorrespondenceDeliveryNoticeReceivedIntegrationEvent : IntegrationEvent
{
    public required Guid DeliveryNoticeId { get; init; }
    public required Guid EmailThreadId { get; init; }
    public required Guid DraftId { get; init; }
    public Guid? CustomerId { get; init; }
    public required Guid MailboxAccountId { get; init; }

    /// <summary>Quien envió el correo original (destinatario del aviso en la campana).</summary>
    public required Guid SentByUserId { get; init; }

    public required string Recipient { get; init; }

    /// <summary>Ver <c>EmailIntegrationEvents.CorrespondenceDeliveryOutcomes</c>.</summary>
    public required string Outcome { get; init; }

    /// <summary>Ver <c>EmailIntegrationEvents.DeliveryFailureCategories</c>.</summary>
    public string? Category { get; init; }
}
