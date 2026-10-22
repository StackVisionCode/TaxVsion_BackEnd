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

    private static readonly string[] SubjectMarkers =
    [
        "undeliverable",
        "delivery status notification",
        "undelivered mail",
        "mail delivery failed",
        "delivery has failed",
        "returned mail",
        "failure notice",
    ];

    public static bool IsLikely(RawMessage message)
    {
        var localPart = LocalPartOf(message.From);
        foreach (var sender in SystemSenders)
        {
            if (localPart.StartsWith(sender, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var subject = message.Subject ?? string.Empty;
        foreach (var marker in SubjectMarkers)
        {
            if (subject.Contains(marker, StringComparison.OrdinalIgnoreCase))
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
