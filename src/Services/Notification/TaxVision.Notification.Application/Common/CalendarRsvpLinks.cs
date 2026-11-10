namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Los botones Accept / Maybe / Decline del correo de una cita. El token va en el fragmento
/// (<c>#token=...</c>): el navegador no lo manda al servidor, así que no queda en logs ni en Referer.
/// La página pide confirmar con un clic: un escáner de correo que abra el enlace no responde nada.
/// </summary>
public static class CalendarRsvpLinks
{
    public static void AddTo(IDictionary<string, object?> variables, string baseUrl, string token)
    {
        variables["rsvp_accept_link"] = Link(baseUrl, token, "accepted");
        variables["rsvp_maybe_link"] = Link(baseUrl, token, "tentative");
        variables["rsvp_decline_link"] = Link(baseUrl, token, "declined");
    }

    /// <summary>Base del host de la oficina tomada de un enlace ya armado (el de la sala).</summary>
    public static string? BaseOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : null;

    private static string Link(string baseUrl, string token, string answer) =>
        $"{baseUrl.TrimEnd('/')}/rsvp#token={Uri.EscapeDataString(token)}&answer={answer}";
}
