using TaxVision.Connectors.Application.Providers;

namespace TaxVision.Connectors.Application.Sync;

/// <summary>
/// Filtro barato con la metadata que ya tenemos: ¿parece un aviso de no entrega? Solo esos se bajan
/// completos para leer el informe. Un falso positivo cuesta una descarga; uno negativo, un rebote sin ver.
/// </summary>
public static class DeliveryReportCandidate
{
    private static readonly string[] SystemSenders =
    [
        "mailer-daemon",
        "postmaster",
        "microsoftexchange",
        "mail-daemon",
    ];

    // Frases completas, no palabras sueltas: "entrega" o "delivery" solos agarran correo normal.
    private static readonly string[] SubjectMarkers =
    [
        "undeliverable",
        "delivery status notification",
        "undelivered mail",
        "mail delivery failed",
        "delivery has failed",
        "delivery failure",
        "delivery delayed",
        "returned mail",
        "failure notice",
        // Exchange/Outlook y Gmail en español.
        "no se puede entregar",
        "no se pudo entregar",
        "mensaje no entregado",
        "correo no entregado",
        "error de entrega",
        "entrega retrasada",
        "notificación de estado de entrega",
        "notificacion de estado de entrega",
    ];

    public static bool IsLikely(RawMessage message)
    {
        if (IsSystemSender(message.From))
            return true;

        // Gmail e IMAP ya traen el tipo raíz; Graph no (queda null y decide el resto).
        if (string.Equals(message.ContentType, "multipart/report", StringComparison.OrdinalIgnoreCase))
            return true;

        var subject = message.Subject ?? string.Empty;
        foreach (var marker in SubjectMarkers)
        {
            if (subject.Contains(marker, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>¿El remitente es un sistema de correo (mailer-daemon, postmaster, Exchange)?</summary>
    public static bool IsSystemSender(string? address)
    {
        var localPart = LocalPartOf(address);
        foreach (var sender in SystemSenders)
        {
            if (localPart.StartsWith(sender, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string LocalPartOf(string? address)
    {
        if (string.IsNullOrWhiteSpace(address))
            return string.Empty;
        var at = address.IndexOf('@');
        return at > 0 ? address[..at] : address;
    }
}
