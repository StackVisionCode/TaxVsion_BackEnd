namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Dirección postal usada en snapshots de cliente/emisor. Una parte vacía ("") = no informada;
/// el record no cambia de forma para no romper el JSON de las facturas ya emitidas.</summary>
public sealed record Address(string Line1, string? Line2, string City, string State, string Zip, string Country)
{
    public const string UnitedStates = "US";

    // Formas habituales de escribir EE. UU. a mano; todas se guardan como "US".
    private static readonly HashSet<string> UnitedStatesAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        "US",
        "USA",
        "U.S.",
        "U.S.A.",
        "UNITED STATES",
        "UNITED STATES OF AMERICA",
    };

    // Marcador que estampaba un emisor sin dirección antes de F0; en facturas viejas cuenta como vacío.
    private const string LegacyPlaceholder = "—";

    /// <summary>Una sola línea con las partes informadas ("Line1, Line2, City, ST ZIP, Country"); sin
    /// calle, ciudad, estado ni ZIP → null (el PDF no imprime línea de dirección).</summary>
    public string? ToSingleLine()
    {
        var line1 = Line1 == LegacyPlaceholder ? string.Empty : Line1;
        var stateZip = JoinPresent(" ", State, Zip);
        string?[] parts = [line1, Line2, City, stateZip];
        if (parts.All(string.IsNullOrWhiteSpace))
            return null;

        return JoinPresent(", ", [.. parts, Country]);
    }

    /// <summary>True si no tiene calle, ciudad, estado ni ZIP. Método (no propiedad) para que no
    /// entre en el JSON del snapshot.</summary>
    public bool IsBlank() =>
        string.IsNullOrWhiteSpace(Line1)
        && string.IsNullOrWhiteSpace(Line2)
        && string.IsNullOrWhiteSpace(City)
        && string.IsNullOrWhiteSpace(State)
        && string.IsNullOrWhiteSpace(Zip);

    /// <summary>
    /// Arma una dirección con lo que llegue (cada parte opcional, recortada). No valida: los datos ya
    /// vienen validados por su dueño y un valor raro de antes nunca debe romper la proyección. El país
    /// escrito a mano como EE. UU. queda "US". Sin calle, ciudad, estado ni ZIP → null.
    /// </summary>
    public static Address? FromParts(
        string? line1,
        string? line2,
        string? city,
        string? state,
        string? zip,
        string? country
    )
    {
        var line2Value = Clean(line2);
        var address = new Address(
            Clean(line1),
            line2Value.Length > 0 ? line2Value : null,
            Clean(city),
            Clean(state),
            Clean(zip),
            NormalizeCountry(country)
        );

        return address.IsBlank() ? null : address;
    }

    /// <summary>Dirección vacía para snapshots que exigen una (emisor sin dirección cargada).</summary>
    public static Address Empty(string country = UnitedStates) =>
        new(string.Empty, null, string.Empty, string.Empty, string.Empty, country);

    private static string NormalizeCountry(string? country)
    {
        var trimmed = Clean(country);
        if (trimmed.Length == 0 || UnitedStatesAliases.Contains(trimmed))
            return UnitedStates;
        return trimmed.ToUpperInvariant();
    }

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static string JoinPresent(string separator, params string?[] parts) =>
        string.Join(separator, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p!.Trim()));
}
