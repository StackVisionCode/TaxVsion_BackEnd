using System.Text;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El <c>.ics</c> lo leen Outlook, Gmail y Apple Calendar, no una persona: lo que se prueba es la forma
/// exacta (CRLF, folding a 75 octetos, escapes, UID estable, SEQUENCE) porque un byte fuera de sitio
/// se ve como "invitación corrupta" sin más explicación.
/// </summary>
public sealed class CalendarInviteBuilderTests
{
    private static readonly Guid MeetingId = Guid.Parse("5c0a2d6e-9d7b-4c1f-8e2a-3b4c5d6e7f80");
    private static readonly DateTime StartUtc = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime StampUtc = new(2026, 10, 30, 12, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Request_lleva_las_propiedades_obligatorias_en_utc_y_termina_cada_linea_en_crlf()
    {
        var ics = Build(Request());

        Assert.StartsWith("BEGIN:VCALENDAR\r\nVERSION:2.0\r\n", ics);
        Assert.EndsWith("END:VEVENT\r\nEND:VCALENDAR\r\n", ics);
        Assert.Contains("PRODID:-//TaxOffix//Meetings//EN\r\n", ics);
        Assert.Contains("METHOD:REQUEST\r\n", ics);
        Assert.Contains($"UID:meeting:{MeetingId:D}@taxoffix.io\r\n", ics);
        Assert.Contains("DTSTAMP:20261030T123000Z\r\n", ics);
        Assert.Contains("SEQUENCE:0\r\n", ics);
        Assert.Contains("DTSTART:20261105T180000Z\r\n", ics);
        Assert.Contains("DTEND:20261105T184500Z\r\n", ics);
        Assert.Contains("STATUS:CONFIRMED\r\n", ics);
        Assert.Contains("ORGANIZER;CN=María Pérez:mailto:support@taxoffix.io\r\n", ics);
        Assert.Contains("URL:https://manfer.taxoffix.io/meet/abc\r\n", ics);
        // Ningún LF suelto: todos los saltos son CRLF.
        Assert.DoesNotContain("\n", ics.Replace("\r\n", string.Empty));
    }

    [Fact]
    public void Cancel_marca_el_evento_cancelado_y_conserva_el_mismo_uid()
    {
        var request = Request() with { Method = CalendarInviteMethod.Cancel, Sequence = 29_876_543 };

        var attachment = new CalendarInviteBuilder(TestPlatform.Settings).Build(request);
        var ics = Text(attachment);

        Assert.Equal("cancel.ics", attachment.FileName);
        Assert.Equal("text/calendar; method=CANCEL; charset=utf-8", attachment.ContentType);
        Assert.Contains("METHOD:CANCEL\r\n", ics);
        Assert.Contains("STATUS:CANCELLED\r\n", ics);
        Assert.Contains("SEQUENCE:29876543\r\n", ics);
        Assert.Contains($"UID:meeting:{MeetingId:D}@taxoffix.io\r\n", ics);
    }

    /// <summary>Sin duración declarada la cita dura una hora: una invitación sin fin se pinta como de día entero.</summary>
    [Fact]
    public void Sin_duracion_la_cita_dura_sesenta_minutos()
    {
        var ics = Build(Request() with { DurationMinutes = null });

        Assert.Contains("DTEND:20261105T190000Z\r\n", ics);
    }

    [Fact]
    public void Texto_con_coma_punto_y_coma_y_salto_de_linea_se_escapa()
    {
        var ics = Build(Request() with { Summary = "Taxes; review, part 1\nwith notes" });

        // La descripción pasa de 75 octetos y se dobla: se desdobla antes de comparar.
        var unfolded = ics.Replace("\r\n ", string.Empty);
        Assert.Contains("SUMMARY:Taxes\\; review\\, part 1\\nwith notes\r\n", unfolded);
        Assert.Contains("DESCRIPTION:Join: https://manfer.taxoffix.io/meet/abc\\nMeeting code: ABC-123\r\n", unfolded);
    }

    [Fact]
    public void Ninguna_linea_pasa_de_75_octetos_y_la_continuacion_empieza_con_espacio()
    {
        var longTitle = string.Concat(Enumerable.Repeat("Revisión anual de impuestos — ", 6));

        var ics = Build(
            Request() with
            {
                Summary = longTitle,
                AttendeeName = "Un nombre bastante largo para forzar el corte",
            }
        );

        var lines = ics.Split("\r\n", StringSplitOptions.None);
        Assert.All(
            lines,
            line =>
                Assert.True(
                    Encoding.UTF8.GetByteCount(line) <= 75,
                    $"Línea de {Encoding.UTF8.GetByteCount(line)} octetos: {line}"
                )
        );
        Assert.Contains(lines, line => line.StartsWith(' '));
        // Desdoblada (CRLF + espacio fuera), la línea vuelve a ser el valor completo sin caracteres partidos.
        var unfolded = ics.Replace("\r\n ", string.Empty);
        Assert.Contains($"SUMMARY:{longTitle}\r\n", unfolded);
    }

    [Fact]
    public void Un_cn_con_coma_va_entre_comillas()
    {
        var ics = Build(Request() with { OrganizerName = "Pérez, María" });

        Assert.Contains("ORGANIZER;CN=\"Pérez, María\":mailto:support@taxoffix.io\r\n", ics);
    }

    private static string Build(CalendarInviteRequest request) =>
        Text(new CalendarInviteBuilder(TestPlatform.Settings).Build(request));

    private static string Text(BuildingBlocks.Messaging.EmailIntegrationEvents.EmailInlineAttachment attachment) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(attachment.ContentBase64));

    private static CalendarInviteRequest Request() =>
        new(
            CalendarInviteMethod.Request,
            MeetingId,
            "Revisión de la 1040",
            StartUtc,
            45,
            Sequence: 0,
            StampUtc,
            Url: "https://manfer.taxoffix.io/meet/abc",
            MeetingCode: "ABC-123",
            OrganizerName: "María Pérez",
            OrganizerEmail: "support@taxoffix.io",
            AttendeeEmail: "cliente@example.com",
            AttendeeName: "Cliente"
        );
}
