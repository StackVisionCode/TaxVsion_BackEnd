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
///
/// <para>
/// F6 (2026-10-30): ampliado con los datos de la reunión (título, estado, código, fecha,
/// duración, host, zona horaria y quién invitó) para que Notification mande la invitación
/// completa con <c>.ics</c>. Todo lo nuevo es opcional: un publisher anterior a F6 sigue
/// deserializando.
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

    public string? MeetingTitle { get; init; }

    /// <summary>Scheduled | Live. Live = la reunión ya empezó: el correo invita a entrar ahora.</summary>
    public string? MeetingStatus { get; init; }
    public string? ShortCode { get; init; }
    public DateTime? ScheduledForUtc { get; init; }
    public int? DurationMinutes { get; init; }
    public Guid? HostUserId { get; init; }
    public string? HostDisplayName { get; init; }

    /// <summary>Zona IANA de la oficina (null = UTC).</summary>
    public string? TimeZone { get; init; }
    public Guid? InvitedByUserId { get; init; }
    public string? InvitedByDisplayName { get; init; }
}
