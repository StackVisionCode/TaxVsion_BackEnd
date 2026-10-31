using BuildingBlocks.Messaging.EmailIntegrationEvents;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>
/// Arma el adjunto <c>.ics</c> (iCalendar, RFC 5545/5546) de una reunión para que el cliente de correo
/// del invitado la ponga en su calendario: <c>METHOD:REQUEST</c> al invitar o reprogramar,
/// <c>METHOD:CANCEL</c> al cancelar. El <c>UID</c> es estable por reunión y el <c>SEQUENCE</c> crece con
/// cada cambio, que es lo que hace que el calendario actualice la cita en vez de duplicarla.
/// </summary>
public interface ICalendarInviteBuilder
{
    EmailInlineAttachment Build(CalendarInviteRequest request);
}

public enum CalendarInviteMethod
{
    Request,
    Cancel,
}

/// <param name="MeetingId">Base del <c>UID</c>: la misma reunión produce siempre el mismo.</param>
/// <param name="Sequence">0 al invitar; mayor en cada reprogramación y en la cancelación.</param>
/// <param name="DurationMinutes">null = 60 (la reunión no declaró duración).</param>
/// <param name="Url">Enlace para entrar; va también en la descripción junto al código.</param>
/// <param name="OrganizerName">CN del organizador (host de la reunión).</param>
/// <param name="OrganizerEmail">mailto del organizador (remitente del sistema).</param>
public sealed record CalendarInviteRequest(
    CalendarInviteMethod Method,
    Guid MeetingId,
    string Summary,
    DateTime StartUtc,
    int? DurationMinutes,
    int Sequence,
    DateTime StampUtc,
    string? Url,
    string? MeetingCode,
    string? OrganizerName,
    string OrganizerEmail,
    string AttendeeEmail,
    string? AttendeeName
);
