using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Duplicados entre filas del mismo chunk, con las reglas del alta (B-17) más el identificador fiscal:
/// la fila repetida es la que la API rechazaría si la anterior ya estuviera creada.
/// </summary>
public sealed class ImportChunkDuplicateTracker
{
    private readonly HashSet<string> _blindIndexes = [];
    private readonly HashSet<string> _spouseBlindIndexes = [];
    private readonly HashSet<string> _emails = [];

    // Por teléfono + nombre y por nombre + fecha, los segundos nombres ya vistos: casa si alguno es compatible.
    private readonly Dictionary<(string Phone, string Name), List<string?>> _phoneNames = [];
    private readonly Dictionary<(string Name, DateOnly Dob), List<string?>> _nameDobs = [];

    /// <summary>Registra la fila si no repite a otra; si la repite devuelve el motivo y no la registra.</summary>
    public string? TryAdd(CustomerDuplicateRules.Keys keys, string? blindIndex, string? spouseBlindIndex = null)
    {
        (string, string)? phoneName =
            keys.PhoneE164 is not null && keys.Name is not null ? (keys.PhoneE164, keys.Name) : null;
        (string, DateOnly)? nameDob = keys.Name is not null && keys.DateOfBirth is { } dob ? (keys.Name, dob) : null;

        if (blindIndex is not null && _blindIndexes.Contains(blindIndex))
            return "Another row in the same chunk has the same tax identifier.";
        if (keys.Email is not null && _emails.Contains(keys.Email))
            return "Another row in the same chunk has the same email.";
        if (phoneName is { } pn && Seen(_phoneNames, pn, keys.MiddleName))
            return "Another row in the same chunk has the same phone and name.";
        if (nameDob is { } nd && Seen(_nameDobs, nd, keys.MiddleName))
            return "Another row in the same chunk has the same name + date of birth.";
        if (spouseBlindIndex is not null && _spouseBlindIndexes.Contains(spouseBlindIndex))
            return "Another row in the same chunk has the same spouse tax identifier.";

        // Una fila rechazada no se crea: no debe bloquear a las siguientes.
        if (blindIndex is not null)
            _blindIndexes.Add(blindIndex);
        if (keys.Email is not null)
            _emails.Add(keys.Email);
        if (phoneName is { } addPn)
            Remember(_phoneNames, addPn, keys.MiddleName);
        if (nameDob is { } addNd)
            Remember(_nameDobs, addNd, keys.MiddleName);
        if (spouseBlindIndex is not null)
            _spouseBlindIndexes.Add(spouseBlindIndex);
        return null;
    }

    private static bool Seen<TKey>(Dictionary<TKey, List<string?>> seen, TKey key, string? middle)
        where TKey : notnull =>
        seen.TryGetValue(key, out var middles)
        && middles.Exists(other => CustomerDuplicateRules.CompatibleMiddleNames(other, middle));

    private static void Remember<TKey>(Dictionary<TKey, List<string?>> seen, TKey key, string? middle)
        where TKey : notnull
    {
        if (!seen.TryGetValue(key, out var middles))
            seen[key] = middles = [];
        middles.Add(middle);
    }
}
