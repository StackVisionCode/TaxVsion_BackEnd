using System.Globalization;
using System.Text;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Platform;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// iCalendar a mano, sin librería: el archivo son diez líneas fijas y lo que importa (UID estable,
/// SEQUENCE, CRLF, folding a 75 octetos, escapes) se controla mejor viéndolo que configurándolo.
///
/// <para>Las horas van en UTC (sufijo <c>Z</c>): el calendario del invitado las pinta en su zona, así
/// que no hace falta embeber un VTIMEZONE. El <c>UID</c> lleva el dominio del producto
/// (<c>meeting:{id}@{dominio}</c>) para que no choque con el de ningún otro sistema.</para>
/// </summary>
public sealed class CalendarInviteBuilder(IPlatformSettings platform) : ICalendarInviteBuilder
{
    public const int DefaultDurationMinutes = 60;
    private const int MaxLineOctets = 75;
    private const string CrLf = "\r\n";

    public EmailInlineAttachment Build(CalendarInviteRequest request)
    {
        var settings = platform.Current;
        var method = request.Method == CalendarInviteMethod.Cancel ? "CANCEL" : "REQUEST";
        var start = DateTime.SpecifyKind(request.StartUtc, DateTimeKind.Utc);
        var end = start.AddMinutes(request.DurationMinutes is > 0 and var minutes ? minutes : DefaultDurationMinutes);

        var lines = new List<string>
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            $"PRODID:-//{Escape(settings.BrandName)}//Meetings//EN",
            "CALSCALE:GREGORIAN",
            $"METHOD:{method}",
            "BEGIN:VEVENT",
            $"UID:{Uid(request.MeetingId, settings.BaseDomain)}",
            $"DTSTAMP:{Stamp(request.StampUtc)}",
            $"SEQUENCE:{request.Sequence.ToString(CultureInfo.InvariantCulture)}",
            $"DTSTART:{Stamp(start)}",
            $"DTEND:{Stamp(end)}",
            $"SUMMARY:{Escape(request.Summary)}",
        };

        var description = Description(request);
        if (description.Length > 0)
            lines.Add($"DESCRIPTION:{Escape(description)}");
        if (!string.IsNullOrWhiteSpace(request.Url))
            lines.Add($"URL:{request.Url.Trim()}");

        lines.Add($"ORGANIZER{Cn(request.OrganizerName)}:mailto:{request.OrganizerEmail.Trim()}");
        lines.Add(
            $"ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE{Cn(request.AttendeeName)}:mailto:{request.AttendeeEmail.Trim()}"
        );
        lines.Add(request.Method == CalendarInviteMethod.Cancel ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");
        lines.Add("END:VEVENT");
        lines.Add("END:VCALENDAR");

        var content = string.Concat(lines.Select(line => Fold(line) + CrLf));
        var fileName = request.Method == CalendarInviteMethod.Cancel ? "cancel.ics" : "invite.ics";
        return new EmailInlineAttachment(
            fileName,
            $"text/calendar; method={method}; charset=utf-8",
            Convert.ToBase64String(Encoding.UTF8.GetBytes(content))
        );
    }

    public static string Uid(Guid meetingId, string baseDomain) =>
        $"meeting:{meetingId:D}@{PlatformSettingsSnapshot.NormalizeHost(baseDomain)}";

    private static string Description(CalendarInviteRequest request)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(request.Url))
            parts.Add($"Join: {request.Url.Trim()}");
        if (!string.IsNullOrWhiteSpace(request.MeetingCode))
            parts.Add($"Meeting code: {request.MeetingCode.Trim()}");
        return string.Join("\n", parts);
    }

    private static string Cn(string? name) =>
        string.IsNullOrWhiteSpace(name) ? string.Empty : $";CN={QuoteParam(name.Trim())}";

    // Valor de parámetro (RFC 5545 §3.2): entre comillas si lleva ':' ';' ',' y sin comillas dentro.
    private static string QuoteParam(string value)
    {
        var clean = value.Replace("\"", string.Empty).Replace("\r", string.Empty).Replace("\n", " ");
        return clean.IndexOfAny([':', ';', ',']) >= 0 ? $"\"{clean}\"" : clean;
    }

    private static string Stamp(DateTime utc) =>
        DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    // TEXT (RFC 5545 §3.3.11): '\' ';' ',' se escapan y un salto de línea se escribe como "\n".
    public static string Escape(string value) =>
        value
            .Replace("\\", "\\\\")
            .Replace("\r\n", "\n")
            .Replace("\r", "\n")
            .Replace(";", "\\;")
            .Replace(",", "\\,")
            .Replace("\n", "\\n");

    /// <summary>
    /// Folding (RFC 5545 §3.1): ninguna línea pasa de 75 octetos; la continuación empieza con un espacio.
    /// Se corta por octetos UTF-8 sin partir un carácter, que es lo que rompe los acentos en un folding
    /// hecho por caracteres.
    /// </summary>
    public static string Fold(string line)
    {
        if (Encoding.UTF8.GetByteCount(line) <= MaxLineOctets)
            return line;

        var result = new StringBuilder();
        var octets = 0;
        var limit = MaxLineOctets;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > limit)
            {
                result.Append(CrLf).Append(' ');
                octets = 1; // el espacio de continuación cuenta
                limit = MaxLineOctets;
            }

            result.Append(rune.ToString());
            octets += size;
        }

        return result.ToString();
    }
}
