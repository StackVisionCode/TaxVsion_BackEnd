using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Las reglas de duplicado del alta, en un solo lugar (B-17): alta, edición, formulario e import deciden igual.
/// Prioridad: Email; luego Teléfono + Nombre; luego Nombre + Fecha de nacimiento. Sin archivados.
/// Nombre de persona = nombre y apellido, con segundo nombre compatible.
/// </summary>
public static class CustomerDuplicateRules
{
    public const string ByEmail = "Email";
    public const string ByPhoneAndName = "Phone+Name";
    public const string ByNameAndDateOfBirth = "Name+DOB";

    /// <summary>Lo que se compara, ya normalizado (email y nombre en minúsculas, teléfono en E.164).</summary>
    public sealed record Keys(
        string? Email,
        string? PhoneE164,
        string? Name,
        DateOnly? DateOfBirth,
        string? MiddleName = null
    );

    /// <summary>Lo mínimo de un cliente existente para aplicar las reglas.</summary>
    public sealed record ExistingCustomer(
        Guid Id,
        string DisplayName,
        string NameKey,
        string? MiddleName,
        DateOnly? DateOfBirth,
        string? EmailNormalized,
        string? PhoneE164
    );

    public static Keys KeysOf(CustomerDuplicateCandidate candidate) =>
        new(
            NormalizeEmail(candidate.Email),
            string.IsNullOrWhiteSpace(candidate.PhoneE164) ? null : candidate.PhoneE164.Trim(),
            NormalizeName(candidate.DisplayName),
            candidate.DateOfBirth,
            NormalizeName(candidate.MiddleName)
        );

    /// <summary>
    /// Nombre que se compara: persona = nombre y apellido (el segundo nombre va aparte); empresa = razón social.
    /// </summary>
    public static (string? Name, string? MiddleName) NameOf(PersonalName? person, string? legalName) =>
        person is not null
            ? (NormalizeName($"{person.FirstName} {person.LastName}"), NormalizeName(person.MiddleName))
            : (NormalizeName(legalName), null);

    /// <summary>
    /// Dos segundos nombres son de la misma persona si falta uno, son iguales o uno es la inicial del otro
    /// ("Ali" y "A."). Distintos ("A." y "B.") separan: padre e hijo con el teléfono de la casa.
    /// </summary>
    public static bool CompatibleMiddleNames(string? a, string? b)
    {
        var left = a?.Trim().TrimEnd('.').ToLowerInvariant();
        var right = b?.Trim().TrimEnd('.').ToLowerInvariant();
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right) || left == right)
            return true;
        return (left.Length == 1 || right.Length == 1) && left[0] == right[0];
    }

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string? NormalizeName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim().ToLowerInvariant();

    /// <summary>
    /// E-4: llave del candado de alta, con la misma prioridad que las reglas: email; sin email, teléfono + nombre;
    /// si no, fecha de nacimiento + nombre. Así dos altas del mismo cliente sin email también se esperan.
    /// </summary>
    public static string LockKey(Keys keys)
    {
        if (keys.Email is not null)
            return keys.Email;
        if (keys.PhoneE164 is not null)
            return $"phone:{keys.PhoneE164}|{keys.Name}";
        return keys.DateOfBirth is { } dob ? $"birth:{dob:yyyy-MM-dd}|{keys.Name}" : $"name:{keys.Name}";
    }

    /// <summary>El primer existente que casa por la regla de mayor prioridad, o null.</summary>
    public static (ExistingCustomer Customer, string MatchedBy)? Match(
        Keys keys,
        IReadOnlyCollection<ExistingCustomer> existing
    )
    {
        // El correo manda: es la llave del portal.
        if (keys.Email is not null)
        {
            var byEmail = existing.FirstOrDefault(c => c.EmailNormalized == keys.Email);
            if (byEmail is not null)
                return (byEmail, ByEmail);
        }

        // El teléfono solo no alcanza: una familia comparte el de la casa. Tiene que coincidir el nombre.
        if (keys.PhoneE164 is not null && keys.Name is not null)
        {
            var byPhone = existing.FirstOrDefault(c => c.PhoneE164 == keys.PhoneE164 && SameName(c, keys));
            if (byPhone is not null)
                return (byPhone, ByPhoneAndName);
        }

        if (keys.Name is not null && keys.DateOfBirth is not null)
        {
            var byName = existing.FirstOrDefault(c => c.DateOfBirth == keys.DateOfBirth && SameName(c, keys));
            if (byName is not null)
                return (byName, ByNameAndDateOfBirth);
        }

        return null;
    }

    private static bool SameName(ExistingCustomer customer, Keys keys) =>
        customer.NameKey.Equals(keys.Name, StringComparison.OrdinalIgnoreCase)
        && CompatibleMiddleNames(customer.MiddleName, keys.MiddleName);
}
