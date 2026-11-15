using BuildingBlocks.TimeZones;

namespace TaxVision.Codes.Domain.Definitions;

/// <summary>
/// Zona comercial de la plataforma (P30): la LLC está en Miami. Los códigos de plataforma vencen al final del día
/// elegido en esta zona, y el correo la escribe.
/// </summary>
public static class CommercialTimeZone
{
    public const string Id = "America/New_York";

    /// <summary>Último segundo de ese día en la zona comercial, en UTC.</summary>
    public static DateTime EndOfDayUtc(DateOnly day) => LocalDay.EndUtc(day, ResolvedTimeZone.Resolve(Id));
}
