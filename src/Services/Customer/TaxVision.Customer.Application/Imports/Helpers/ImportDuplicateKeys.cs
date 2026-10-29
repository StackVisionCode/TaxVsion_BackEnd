using System.Globalization;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Llaves de duplicado de una fila del import, armadas con los mismos value objects que el alta (B-17):
/// email normalizado, teléfono E.164, nombre para mostrar (persona: First Middle Last; empresa: razón social) y DOB.
/// </summary>
public static class ImportDuplicateKeys
{
    /// <summary>Desde la fila cruda; cada llave se calcula por separado y queda null si no es válida.</summary>
    public static CustomerDuplicateRules.Keys From(ImportCustomerRow row) =>
        new(EmailOf(row.Email), PhoneOf(row.Phone), NameOf(row), DateOfBirthOf(row.DateOfBirth));

    /// <summary>Desde la fila ya parseada: mismas llaves que daría la fila cruda.</summary>
    internal static CustomerDuplicateRules.Keys From(ParsedRow row) =>
        new(
            row.Email.NormalizedValue,
            row.Phone?.E164Value,
            CustomerDuplicateRules.NormalizeName(
                row.Kind == CustomerKind.Business ? row.BusinessIdentity?.LegalName : row.PersonalName?.DisplayName
            ),
            row.DateOfBirth
        );

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

    private static string? NameOf(ImportCustomerRow row)
    {
        if (Enum.TryParse<CustomerKind>(row.Kind, ignoreCase: true, out var kind) && kind == CustomerKind.Business)
            return CustomerDuplicateRules.NormalizeName(row.LegalName);

        var name = PersonalName.Create(
            row.FirstName ?? string.Empty,
            row.LastName ?? string.Empty,
            row.MiddleName,
            row.Prefix,
            row.Suffix
        );
        return name.IsSuccess ? CustomerDuplicateRules.NormalizeName(name.Value.DisplayName) : null;
    }

    private static DateOnly? DateOfBirthOf(string? raw) =>
        DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob) ? dob : null;
}
