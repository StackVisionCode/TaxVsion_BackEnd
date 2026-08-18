using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// La sala de una cita virtual quedó creada. Lo publica Communication (Node.js), así que
/// <see cref="MessageIdentityAttribute"/> mapea el tipo al string que ese servicio escribe en la
/// propiedad AMQP <c>type</c> — sin él, Wolverine buscaría por el nombre del tipo CLR y nunca
/// encontraría nada.
///
/// <para>
/// Los ids viajan como <c>string</c> y no como <c>Guid</c>, y el tipo no hereda de
/// <c>IntegrationEvent</c>: el emisor es TypeScript y manda camelCase con ids en texto. Declararlo con
/// la forma del emisor y convertir en el consumer es explícito; heredar la base haría que
/// <c>TenantId</c> llegara como <c>Guid.Empty</c> sin que nada fallara.
/// </para>
/// </summary>
[MessageIdentity("communication.meeting.linked_to_appointment.v1")]
public sealed record MeetingLinkedToAppointmentIntegrationEvent
{
    public string EventId { get; init; } = string.Empty;

    public string TenantId { get; init; } = string.Empty;

    public string CorrelationId { get; init; } = string.Empty;

    public string AppointmentId { get; init; } = string.Empty;

    public string MeetingId { get; init; } = string.Empty;

    public string ShortCode { get; init; } = string.Empty;

    public string? ScheduledForUtc { get; init; }
}
