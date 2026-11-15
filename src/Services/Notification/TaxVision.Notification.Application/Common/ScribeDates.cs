using System.Globalization;

namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Fechas tipadas para Scribe (Fase 5, P18): en vez de texto ya formateado viaja un marcador que Scribe imprime en
/// el idioma y la zona de quien lee, con cualquier plantilla (sembrada o de una oficina).
/// </summary>
public static class ScribeDates
{
    /// <summary>Un instante: <c>{"$instant": "2026-11-18T14:00:00.0000000Z"}</c>.</summary>
    public static Dictionary<string, object?> Instant(DateTime instantUtc) =>
        new()
        {
            ["$instant"] = DateTime
                .SpecifyKind(instantUtc, DateTimeKind.Utc)
                .ToString("O", CultureInfo.InvariantCulture),
        };

    public static Dictionary<string, object?>? Instant(DateTime? instantUtc) =>
        instantUtc is { } value ? Instant(value) : null;

    /// <summary>Una fecha civil (un vencimiento): <c>{"$date": "2026-11-15"}</c>, sin zona.</summary>
    public static Dictionary<string, object?> Date(DateOnly date) =>
        new() { ["$date"] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };

    public static Dictionary<string, object?>? Date(DateOnly? date) => date is { } value ? Date(value) : null;
}
