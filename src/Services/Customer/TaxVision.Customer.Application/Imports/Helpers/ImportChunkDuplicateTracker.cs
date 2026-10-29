using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Duplicados entre filas del mismo chunk, con las reglas del alta (B-17) más el identificador fiscal:
/// la fila repetida es la que la API rechazaría si la anterior ya estuviera creada.
/// </summary>
public sealed class ImportChunkDuplicateTracker
{
    private readonly HashSet<string> _blindIndexes = [];
    private readonly HashSet<string> _emails = [];
    private readonly HashSet<(string Phone, string Name)> _phoneNames = [];
    private readonly HashSet<(string Name, DateOnly Dob)> _nameDobs = [];

    /// <summary>Registra la fila si no repite a otra; si la repite devuelve el motivo y no la registra.</summary>
    public string? TryAdd(CustomerDuplicateRules.Keys keys, string? blindIndex)
    {
        (string, string)? phoneName =
            keys.PhoneE164 is not null && keys.Name is not null ? (keys.PhoneE164, keys.Name) : null;
        (string, DateOnly)? nameDob = keys.Name is not null && keys.DateOfBirth is { } dob ? (keys.Name, dob) : null;

        if (blindIndex is not null && _blindIndexes.Contains(blindIndex))
            return "Another row in the same chunk has the same tax identifier.";
        if (keys.Email is not null && _emails.Contains(keys.Email))
            return "Another row in the same chunk has the same email.";
        if (phoneName is { } pn && _phoneNames.Contains(pn))
            return "Another row in the same chunk has the same phone and name.";
        if (nameDob is { } nd && _nameDobs.Contains(nd))
            return "Another row in the same chunk has the same name + date of birth.";

        // Una fila rechazada no se crea: no debe bloquear a las siguientes.
        if (blindIndex is not null)
            _blindIndexes.Add(blindIndex);
        if (keys.Email is not null)
            _emails.Add(keys.Email);
        if (phoneName is { } addPn)
            _phoneNames.Add(addPn);
        if (nameDob is { } addNd)
            _nameDobs.Add(addNd);
        return null;
    }
}
