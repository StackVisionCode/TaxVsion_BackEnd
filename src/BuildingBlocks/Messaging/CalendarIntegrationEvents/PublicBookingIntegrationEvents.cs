using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.CalendarIntegrationEvents;

/// <summary>El visitante sin cuenta, tal como lo escribió. <c>TimeZoneId</c> es la zona que eligió en la página.</summary>
public sealed record PublicBookingVisitor(
    string Name,
    string Email,
    string Company,
    string? OfficeSize,
    string? Phone,
    string? Interest,
    string TimeZoneId
);

/// <summary>Quien da la demo. <c>TimeZoneId</c> sale de su disponibilidad; sin ella, la del visitante.</summary>
public sealed record PublicBookingHost(
    Guid UserId,
    string DisplayName,
    string NotificationEmail,
    string MeetingUrl,
    string TimeZoneId
);

/// <summary>"How did you hear about us?": el código estable y sus etiquetas vigentes.</summary>
public sealed record PublicBookingHeardAbout(string Code, string LabelEn, string? LabelEs, string? Detail);

/// <summary>Por dónde llegó: botón del landing, UTM y solo el dominio del referer.</summary>
public sealed record PublicBookingAttribution(
    string EntrySource,
    string? UtmSource,
    string? UtmMedium,
    string? UtmCampaign,
    string? ReferrerDomain
);

/// <summary>Contenido cifrado de un secreto de la reserva (código o token de gestión).</summary>
public sealed record PublicBookingSecretPayload(string Value);

/// <summary>
/// Esquemas y ataduras de los secretos de la reserva pública. El código y el token viajan solo cifrados
/// (atadura por clave): copiados a otro evento o a otra emisión no se descifran.
/// </summary>
public static class PublicBookingPayloadSchemas
{
    public const string VerificationCode = "calendar.public_booking_code.v1";
    public const string ManagementToken = "calendar.public_booking_management_token.v1";

    public static ProtectedPayloadBinding CodeBinding(Guid tenantId, Guid bookingId, int send) =>
        new(tenantId, null, null, IdempotencyKey: $"calendar.public_booking_code:{bookingId:N}:{send}");

    public static ProtectedPayloadBinding ManagementTokenBinding(Guid tenantId, Guid bookingId, int version) =>
        new(tenantId, null, null, IdempotencyKey: $"calendar.public_booking_token:{bookingId:N}:{version}");
}

/// <summary>Se pidió una demo: hay que mandar el código al visitante. Notification lo envía como confidencial.</summary>
[MessageIdentity("calendar.public_booking_code_requested.v1")]
public sealed record PublicBookingCodeRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required string PageTitle { get; init; }
    public required string Language { get; init; }
    public required string VisitorName { get; init; }
    public required string VisitorEmail { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }

    /// <summary>Número de envío (1 a 3): parte de la atadura del código y de la clave de idempotencia.</summary>
    public required int Send { get; init; }

    public required ProtectedPayload ProtectedCode { get; init; }
}

/// <summary>Se confirmó la demo: correo al visitante (con .ics) y al anfitrión.</summary>
[MessageIdentity("calendar.public_booking_confirmed.v1")]
public sealed record PublicBookingConfirmedIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required Guid AppointmentId { get; init; }
    public required string PageTitle { get; init; }
    public required DateTime StartUtc { get; init; }
    public required int DurationMinutes { get; init; }
    public required string Language { get; init; }
    public required PublicBookingVisitor Visitor { get; init; }
    public required PublicBookingHost Host { get; init; }
    public required PublicBookingHeardAbout HeardAbout { get; init; }
    public required PublicBookingAttribution Attribution { get; init; }
    public required bool MarketingOptIn { get; init; }
    public required string IcsUid { get; init; }
    public required int IcsSequence { get; init; }
    public IReadOnlyList<int> IcsAlarmMinutes { get; init; } = [];
    public required int ManagementTokenVersion { get; init; }
    public required ProtectedPayload ProtectedManagementToken { get; init; }
}

/// <summary>La demo cambió de horario (la movió el visitante o el equipo).</summary>
[MessageIdentity("calendar.public_booking_rescheduled.v1")]
public sealed record PublicBookingRescheduledIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required Guid AppointmentId { get; init; }
    public required string PageTitle { get; init; }
    public required DateTime PreviousStartUtc { get; init; }
    public required DateTime StartUtc { get; init; }
    public required int DurationMinutes { get; init; }
    public required string Language { get; init; }
    public required PublicBookingVisitor Visitor { get; init; }
    public required PublicBookingHost Host { get; init; }
    public required string ChangedBy { get; init; }
    public string? Reason { get; init; }
    public required string IcsUid { get; init; }
    public required int IcsSequence { get; init; }
    public IReadOnlyList<int> IcsAlarmMinutes { get; init; } = [];
    public required int ManagementTokenVersion { get; init; }
    public required ProtectedPayload ProtectedManagementToken { get; init; }
}

/// <summary>La demo se canceló: correo con .ics CANCEL a las dos partes.</summary>
[MessageIdentity("calendar.public_booking_cancelled.v1")]
public sealed record PublicBookingCancelledIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required string PageTitle { get; init; }
    public required DateTime StartUtc { get; init; }
    public required int DurationMinutes { get; init; }
    public required string Language { get; init; }
    public required PublicBookingVisitor Visitor { get; init; }
    public required PublicBookingHost Host { get; init; }
    public required string CancelledBy { get; init; }
    public string? Reason { get; init; }
    public required string IcsUid { get; init; }
    public required int IcsSequence { get; init; }
}

/// <summary>
/// Quien ya tiene una demo confirmada pidió otra: la pantalla respondió como a cualquiera y al dueño del email se le
/// recuerda su demo con un enlace de gestión nuevo (DEMO-11). Notification lo envía como confidencial.
/// </summary>
[MessageIdentity("calendar.public_booking_already_booked.v1")]
public sealed record PublicBookingAlreadyBookedIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required string PageTitle { get; init; }
    public required DateTime StartUtc { get; init; }
    public required int DurationMinutes { get; init; }
    public required string Language { get; init; }
    public required PublicBookingVisitor Visitor { get; init; }
    public required PublicBookingHost Host { get; init; }
    public required int ManagementTokenVersion { get; init; }
    public required ProtectedPayload ProtectedManagementToken { get; init; }
}

/// <summary>Recordatorio de la demo al visitante, con el enlace de la reunión y uno de gestión nuevo.</summary>
[MessageIdentity("calendar.public_booking_reminder_due.v1")]
public sealed record PublicBookingReminderDueIntegrationEvent : IntegrationEvent
{
    public required Guid BookingId { get; init; }
    public required string PageTitle { get; init; }
    public required DateTime StartUtc { get; init; }
    public required int DurationMinutes { get; init; }
    public required string Language { get; init; }
    public required PublicBookingVisitor Visitor { get; init; }
    public required PublicBookingHost Host { get; init; }
    public required int HoursBefore { get; init; }
    public required int ManagementTokenVersion { get; init; }
    public required ProtectedPayload ProtectedManagementToken { get; init; }
}
