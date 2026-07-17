using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CommunicationIntegrationEvents;

/// <summary>
/// Publicado por el microservicio Communication (Node.js) — no un servicio Wolverine —
/// cuando un Host/Cohost invita a alguien a un meeting (Fase Backend 5 de Communication).
/// <see cref="MessageIdentityAttribute"/> mapea explícitamente este tipo al string de
/// evento que Communication realmente escribe en la propiedad AMQP <c>type</c>
/// (<c>communication.meeting.invitation_created.v1</c>) — sin este atributo, Wolverine
/// intentaría resolver el mensaje por el nombre completo del tipo CLR, que nunca va a
/// matchear un publisher fuera de .NET.
///
/// <para>
/// Nota de compatibilidad: el evento de origen (TS) usa <c>occurredOnUtc</c> (string
/// ISO-8601) y <c>correlationId</c> (nombres exactos, distinta forma), mientras que la
/// base <see cref="IntegrationEvent"/> de este lado expone <c>OccurredOn</c>/
/// <c>CorrelationId</c> (PascalCase, sin mapeo JSON explícito). <c>TenantId</c> también
/// difiere: aquí es <c>Guid</c>, en el evento TS es un string. Esto es la primera vez que
/// un evento Node→.NET cruza a este servicio — no hay precedente probado en el repo — así
/// que estos campos heredados pueden deserializar con sus defaults (Guid.Empty /
/// DateTime.UtcNow) en vez del valor real hasta que se verifique end-to-end.
/// </para>
/// </summary>
[MessageIdentity("communication.meeting.invitation_created.v1")]
public sealed record MeetingInvitationCreatedIntegrationEvent : IntegrationEvent
{
    public required Guid InvitationId { get; init; }
    public required Guid MeetingId { get; init; }
    public required string InviteeKind { get; init; } // Employee | Customer | External
    public Guid? InviteeUserId { get; init; }
    public string? InviteeEmail { get; init; }
    public string? InviteeName { get; init; }
    public required string TokenHash { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required string JoinUrl { get; init; }
}
