using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// P8: el campo Date del firmado es la fecha civil en la zona congelada al enviar. Una solicitud sin zona (enviada
/// antes) conserva el día UTC de siempre. Los sellos y la evidencia siguen en UTC.
/// </summary>
public static class SealedFieldDates
{
    public static DateOnly DateFor(DateTime signedAtUtc, string? displayTimeZoneId) =>
        displayTimeZoneId is null
            ? DateOnly.FromDateTime(signedAtUtc)
            : CivilDate.Of(signedAtUtc, ResolvedTimeZone.Resolve(displayTimeZoneId));

    /// <summary>"Chicago time, GMT-6" para la línea del certificado; null en solicitudes sin zona congelada.</summary>
    public static string? CertificateZone(string? displayTimeZoneId, DateTime completedAtUtc) =>
        displayTimeZoneId is null
            ? null
            : ZonedTimeFormatter.ZoneLabel(ResolvedTimeZone.Resolve(displayTimeZoneId), completedAtUtc);
}
