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
public sealed record AppointmentRecipient(string Email, Guid? UserId);

/// <summary>Se agendó una cita. Si es serie, <c>StartUtc</c> es el de su primera ocurrencia.</summary>
public sealed record AppointmentScheduledIntegrationEvent : IntegrationEvent
{
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

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

public sealed record AppointmentCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Scope { get; init; }
    public DateTime? OriginalStartUtc { get; init; }
    public string? Reason { get; init; }

    public IReadOnlyList<AppointmentRecipient> Recipients { get; init; } = [];
}

/// <summary>Se canceló una ocurrencia suelta; la serie sigue viva.</summary>
public sealed record OccurrenceCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required DateTime OriginalStartUtc { get; init; }
}

/// <summary>
/// La serie se partió en dos. Para el consumidor no es un update: es una serie que terminó y otra que
/// empezó, y sin este evento Reminder no sabe que los avisos posteriores al corte cambian de dueño.
/// </summary>
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
public sealed record AppointmentAttendeeAddedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string AttendeeKind { get; init; }
    public Guid? UserId { get; init; }
    public Guid? CustomerId { get; init; }
    public string? Email { get; init; }
    public required string Title { get; init; }
    public DateTime? StartUtc { get; init; }
    public required string TimeZoneId { get; init; }
    public required bool IsRecurring { get; init; }
    public required bool IsVirtual { get; init; }
}

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
public sealed record AppointmentMeetingRoomRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required string Title { get; init; }
    public required Guid OrganizerUserId { get; init; }
    public DateTime? StartUtc { get; init; }
}

public sealed record AppointmentStartingSoonIntegrationEvent : IntegrationEvent
{
    public required Guid AppointmentId { get; init; }
    public required DateTime StartUtc { get; init; }
    public IReadOnlyList<Guid> AttendeeUserIds { get; init; } = [];
}
