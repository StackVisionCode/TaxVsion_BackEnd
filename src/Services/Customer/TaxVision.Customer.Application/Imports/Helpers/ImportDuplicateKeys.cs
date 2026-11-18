using System.Globalization;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Llaves de duplicado de una fila del import, armadas con los mismos value objects que el alta (B-17):
/// email normalizado, teléfono E.164, nombre (persona: nombre y apellido, el segundo aparte; empresa: razón social) y DOB.
/// </summary>
public static class ImportDuplicateKeys
{
    /// <summary>Desde la fila cruda; cada llave se calcula por separado y queda null si no es válida.</summary>
    public static CustomerDuplicateRules.Keys From(ImportCustomerRow row)
    {
        var (name, middle) = NameOf(row);
        return new(EmailOf(row.Email), PhoneOf(row.Phone), name, DateOfBirthOf(row.DateOfBirth), middle);
    }

    /// <summary>Desde la fila ya parseada: mismas llaves que daría la fila cruda.</summary>
    internal static CustomerDuplicateRules.Keys From(ParsedRow row)
    {
        var (name, middle) = CustomerDuplicateRules.NameOf(
            row.Kind == CustomerKind.Business ? null : row.PersonalName,
            row.BusinessIdentity?.LegalName
        );
        return new(row.Email?.NormalizedValue, row.Phone?.E164Value, name, row.DateOfBirth, middle);
    }

    private static string? EmailOf(string? raw)
    {
        var email = EmailAddress.Create(raw ?? string.Empty);
        return email.IsSuccess ? email.Value.NormalizedValue : null;
    }

    // Mismo saneo de formatos humanos que el parser del import antes del VO estricto.
    private static string? PhoneOf(string? raw)
    {
        var e164 = IdentifierNormalizer.NormalizePhoneToE164(raw);
        if (string.IsNullOrEmpty(e164))
            return null;
        var phone = PhoneNumber.Create(e164);
        return phone.IsSuccess ? phone.Value.E164Value : null;
    }

    private static (string? Name, string? MiddleName) NameOf(ImportCustomerRow row)
    {
        if (Enum.TryParse<CustomerKind>(row.Kind, ignoreCase: true, out var kind) && kind == CustomerKind.Business)
            return CustomerDuplicateRules.NameOf(null, row.LegalName);

        var name = PersonalName.Create(
            row.FirstName ?? string.Empty,
            row.LastName ?? string.Empty,
            row.MiddleName,
            row.Prefix,
            row.Suffix
        );
        return name.IsSuccess ? CustomerDuplicateRules.NameOf(name.Value, null) : (null, null);
    }

    private static DateOnly? DateOfBirthOf(string? raw) =>
        DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob) ? dob : null;
}
