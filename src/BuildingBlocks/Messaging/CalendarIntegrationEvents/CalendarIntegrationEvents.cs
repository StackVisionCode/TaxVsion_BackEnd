using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CalendarIntegrationEvents;

/// <summary>
/// A quién avisar. Va dentro del evento para que nadie tenga que proyectar los asistentes.
///
/// <para>
/// El <c>UserId</c> es opcional y es lo que separa a un compañero de un cliente invitado: sólo el
/// primero tiene preferencia de notificación que consultar. Ir en pares —y no en dos listas
/// paralelas— evita el error de creer que el tercer email es del tercer usuario.
/// </para>
/// </summary>
// CustomerId (aditivo): el cliente entra a la sala como cliente aunque comparta correo con alguien del equipo.
// DisplayName (aditivo): el saludo de la invitación de la sala. RsvpToken (aditivo): el enlace de
// respuesta de ese invitado; solo en el alta y al mover la cita, null en el resto.
public sealed record AppointmentRecipient(
    string Email,
    Guid? UserId,
    Guid? CustomerId = null,
    string? DisplayName = null,
    string? RsvpToken = null
);

/// <summary>Se agendó una cita. Si es serie, <c>StartUtc</c> es el de su primera ocurrencia.</summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentScheduledIntegrationEvent")]
public sealed record AppointmentScheduledIntegrationEvent : IntegrationEvent
{
    /// <summary>Aditivo: la reservó el cliente en el portal (su nombre). Null si la agendó el equipo.</summary>
    public string? BookedByCustomerName { get; init; }

    /// <summary>Aditivo: viene de aprobar una solicitud del portal; el cliente se entera en vivo.</summary>
    public bool ApprovedRequest { get; init; }

    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public required Guid OrganizerUserId { get; init; }
    public required DateTime StartUtc { get; init; }
    public required DateTime EndUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public required bool IsRecurring { get; init; }
    public required bool IsVirtual { get; init; }
    public Guid? CustomerId { get; init; }
    public int? TaxYear { get; init; }
    public IReadOnlyList<Guid> AttendeeUserIds { get; init; } = [];
    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

/// <summary>
/// La cita se movió.
///
/// <para>
/// <c>Scope</c> viaja siempre: sin él, el consumidor no distingue «se movió una ocurrencia» de «se
/// movió la serie entera» y toma la decisión equivocada — típicamente Reminder, cancelando de más.
/// </para>
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentRescheduledIntegrationEvent")]
public sealed record AppointmentRescheduledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Scope { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public DateTime? PreviousStartUtc { get; init; }
    public required DateTime NewStartUtc { get; init; }
    public required DateTime NewEndUtc { get; init; }
    public required string TimeZoneId { get; init; }

    /// <summary>La sala, si la cita es virtual: Communication la mueve sin buscarla por la cita.</summary>
    public Guid? MeetingId { get; init; }

    /// <summary>Organizador (aditivo): con sala, es el único que recibe el aviso de la cita; el resto, el de la sala.</summary>
    public Guid? OrganizerUserId { get; init; }

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentCancelledIntegrationEvent")]
public sealed record AppointmentCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Scope { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public string? Reason { get; init; }

    /// <summary>La sala, si la cita es virtual: Communication la cancela sin buscarla por la cita.</summary>
    public Guid? MeetingId { get; init; }

    /// <summary>Organizador (aditivo): con sala, es el único que recibe el aviso de la cita; el resto, el de la sala.</summary>
    public Guid? OrganizerUserId { get; init; }

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

/// <summary>
/// Cambiaron título, descripción o lugar de la cita. La hora no: eso es
/// <see cref="AppointmentRescheduledIntegrationEvent"/>. Notification avisa a los asistentes y
/// Communication renombra la sala.
/// </summary>
[MessageIdentity("calendar.appointment_details_changed.v1")]
public sealed record AppointmentDetailsChangedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public required string PreviousTitle { get; init; }
    public string? Description { get; init; }
    public string? Location { get; init; }

    /// <summary>Title, Description y/o Location.</summary>
    public required IReadOnlyList<string> ChangedFields { get; init; }

    public DateTime? StartUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public bool IsRecurring { get; init; }
    public Guid? MeetingId { get; init; }
    public required Guid OrganizerUserId { get; init; }

    /// <summary>Quien editó: no se le avisa de su propio cambio.</summary>
    public required Guid ChangedByUserId { get; init; }

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

/// <summary>Un cliente pidió una cita en el portal; la oficina debe aprobarla, asignarla o rechazarla.</summary>
[MessageIdentity("calendar.booking_requested.v1")]
public sealed record AppointmentBookingRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public DateTime? StartUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public required Guid OrganizerUserId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? CustomerName { get; init; }
}

/// <summary>La oficina rechazó la solicitud del cliente. <c>Recipients</c>: el cliente (y externos) con correo.</summary>
[MessageIdentity("calendar.booking_declined.v1")]
public sealed record AppointmentBookingDeclinedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public DateTime? StartUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public string? Reason { get; init; }

    /// <summary>El cliente de la cita: su portal recibe el aviso en vivo.</summary>
    public Guid? CustomerId { get; init; }

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

/// <summary>Se canceló una ocurrencia suelta; la serie sigue viva.</summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.OccurrenceCancelledIntegrationEvent")]
public sealed record OccurrenceCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required DateTime OriginalStartUtc { get; init; }
}

/// <summary>
/// La serie se partió en dos. Para el consumidor no es un update: es una serie que terminó y otra que
/// empezó, y sin este evento Reminder no sabe que los avisos posteriores al corte cambian de dueño.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.CalendarSeriesSplitIntegrationEvent")]
public sealed record CalendarSeriesSplitIntegrationEvent : IntegrationEvent
{
    public required Guid OriginalSeriesId { get; init; }
    public required Guid NewSeriesId { get; init; }
    public required DateTime CutoffUtc { get; init; }
}

/// <summary>
/// Se sumó un asistente.
///
/// <para>
/// Lleva los datos de la cita porque es el evento que dispara la invitación: la cita se crea vacía y
/// los asistentes se agregan después, así que <see cref="AppointmentScheduledIntegrationEvent"/> sale
/// cuando todavía no hay a quién escribirle.
/// </para>
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentAttendeeAddedIntegrationEvent")]
public sealed record AppointmentAttendeeAddedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string AttendeeKind { get; init; }
    public Guid? UserId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? Email { get; init; }

    /// <summary>Nombre del asistente (aditivo): el saludo de la invitación de la sala.</summary>
    public string? DisplayName { get; init; }

    /// <summary>Su enlace de respuesta (aditivo). Credencial: no se registra en logs.</summary>
    public string? RsvpToken { get; init; }

    public required string Title { get; init; }

    /// <summary>Si es serie, el de su primera ocurrencia; null solo si la regla no da ninguna.</summary>
    public DateTime? StartUtc { get; init; }

    public required string TimeZoneId { get; init; }
    public required bool IsRecurring { get; init; }
    public required bool IsVirtual { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentAttendeeRespondedIntegrationEvent")]
public sealed record AppointmentAttendeeRespondedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required Guid OrganizerUserId { get; init; }
    public Guid? UserId { get; init; }
    public required string Response { get; init; }
}

/// <summary>
/// La cita virtual se quedó sin sala y hay que volver a pedirla.
///
/// <para>
/// Existe aparte de <see cref="AppointmentScheduledIntegrationEvent"/> porque ese evento ya no es sólo
/// para Communication: desde que lleva los destinatarios, republicarlo para reparar una sala reenviaría
/// la invitación a todo el mundo. Éste sólo pide la sala.
/// </para>
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentMeetingRoomRequestedIntegrationEvent")]
public sealed record AppointmentMeetingRoomRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public required Guid OrganizerUserId { get; init; }
    public DateTime? StartUtc { get; init; }
}

[MessageIdentity("BuildingBlocks.Messaging.CalendarIntegrationEvents.AppointmentStartingSoonIntegrationEvent")]
public sealed record AppointmentStartingSoonIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required DateTime StartUtc { get; init; }
    public IReadOnlyList<Guid> AttendeeUserIds { get; init; } = [];
}
