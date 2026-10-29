using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers;

/// <summary>
/// Las reglas de duplicado del alta, en un solo lugar (B-17): alta, edición, formulario e import deciden igual.
/// Prioridad: Email; luego Teléfono + Nombre; luego Nombre + Fecha de nacimiento. Sin archivados.
/// </summary>
public static class CustomerDuplicateRules
{
    public const string ByEmail = "Email";
    public const string ByPhoneAndName = "Phone+Name";
    public const string ByNameAndDateOfBirth = "Name+DOB";

    /// <summary>Lo que se compara, ya normalizado (email y nombre en minúsculas, teléfono en E.164).</summary>
    public sealed record Keys(string? Email, string? PhoneE164, string? Name, DateOnly? DateOfBirth);

    /// <summary>Lo mínimo de un cliente existente para aplicar las reglas.</summary>
    public sealed record ExistingCustomer(
        Guid Id,
        string DisplayName,
        DateOnly? DateOfBirth,
        string EmailNormalized,
        string? PhoneE164
    );

    public static Keys KeysOf(CustomerDuplicateCandidate candidate) =>
        new(
            NormalizeEmail(candidate.Email),
            string.IsNullOrWhiteSpace(candidate.PhoneE164) ? null : candidate.PhoneE164.Trim(),
            NormalizeName(candidate.DisplayName),
            candidate.DateOfBirth
        );

    public static string? NormalizeEmail(string? email) =>
        string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    public static string? NormalizeName(string? displayName) =>
        string.IsNullOrWhiteSpace(displayName) ? null : displayName.Trim().ToLowerInvariant();

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
            var byPhone = existing.FirstOrDefault(c => c.PhoneE164 == keys.PhoneE164 && SameName(c, keys.Name));
            if (byPhone is not null)
                return (byPhone, ByPhoneAndName);
        }

        if (keys.Name is not null && keys.DateOfBirth is not null)
        {
            var byName = existing.FirstOrDefault(c => c.DateOfBirth == keys.DateOfBirth && SameName(c, keys.Name));
            if (byName is not null)
                return (byName, ByNameAndDateOfBirth);
        }

        return null;
    }

    private static bool SameName(ExistingCustomer customer, string name) =>
        customer.DisplayName.Equals(name, StringComparison.OrdinalIgnoreCase);
}
