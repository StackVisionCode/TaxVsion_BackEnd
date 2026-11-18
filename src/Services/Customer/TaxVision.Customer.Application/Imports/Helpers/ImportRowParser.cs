using System.Globalization;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Convierte la fila string-pura del archivo en value objects + enums validados.
/// Estricto en identidad (tipo, nombre o razón social, estructura, email, forma del SSN/EIN): devuelve el primer
/// error. Tolerante en lo opcional: un valor que no sirve se importa vacío y deja un aviso en la fila.
/// </summary>
internal static class ImportRowParser
{
    private static readonly string[] TrueValues = ["true", "yes", "y", "1"];
    private static readonly string[] FalseValues = ["false", "no", "n", "0"];

    public static Result<ParsedRow> Parse(ImportCustomerRow raw)
    {
        var warnings = new List<string>();

        if (raw.ReadError is not null)
            return Result.Failure<ParsedRow>(new Error("Row.UnclosedQuote", raw.ReadError));

        if (!ImportEnum.TryParseName<CustomerKind>(raw.Kind, out var kind))
            return Result.Failure<ParsedRow>(
                new Error("Row.Kind", $"Kind must be Individual or Business. Got '{raw.Kind}'.")
            );

        var (language, languageProvided) = OfferedLanguage(raw.Language, warnings);

        // D-3: el email es opcional; si viene, tiene que ser válido. El contacto mínimo lo decide el agregado.
        EmailAddress? email = null;
        if (!string.IsNullOrWhiteSpace(raw.Email))
        {
            var emailRes = EmailAddress.Create(raw.Email);
            if (emailRes.IsFailure)
                return Result.Failure<ParsedRow>(emailRes.Error);
            email = emailRes.Value;
        }

        var phone = OptionalPhone(raw.Phone, "Phone", warnings);

        PersonalName? personalName = null;
        BusinessIdentity? businessIdentity = null;
        if (kind == CustomerKind.Individual)
        {
            var nameRes = PersonalName.Create(
                firstName: raw.FirstName ?? string.Empty,
                lastName: raw.LastName ?? string.Empty,
                middleName: raw.MiddleName,
                prefix: raw.Prefix,
                suffix: raw.Suffix
            );
            if (nameRes.IsFailure)
                return Result.Failure<ParsedRow>(nameRes.Error);
            personalName = nameRes.Value;
        }
        else
        {
            var bizRes = ParseBusiness(raw, warnings);
            if (bizRes.IsFailure)
                return Result.Failure<ParsedRow>(bizRes.Error);
            businessIdentity = bizRes.Value;
        }

        var dateOfBirth = PastDate(raw.DateOfBirth, "Date of birth", warnings);
        var address = OptionalAddress(raw, warnings);

        FilingStatus? filingStatus = null;
        if (!string.IsNullOrWhiteSpace(raw.FilingStatus))
        {
            if (FilingStatusNames.TryParse(raw.FilingStatus, out var fs))
                filingStatus = fs;
            else
                warnings.Add($"Filing status '{raw.FilingStatus.Trim()}' is not recognized; imported without it.");
        }

        var priorYearAgi = OptionalAgi(raw.PriorYearAgi, warnings);
        var (isReturning, isReturningProvided) = ReturningFlag(raw.IsReturningCustomer, warnings);
        var emailOptIn = OptIn(raw.EmailOptIn, "Email opt-in", warnings);
        var smsOptIn = OptIn(raw.SmsOptIn, "SMS opt-in", warnings);

        var normalizedTaxId = IdentifierNormalizer.NormalizeDigits(raw.TaxIdentifier);
        if (!string.IsNullOrEmpty(normalizedTaxId))
        {
            var ok =
                kind == CustomerKind.Individual
                    ? IdentifierNormalizer.IsValidSsnOrItin(normalizedTaxId)
                    : IdentifierNormalizer.IsValidEin(normalizedTaxId);
            if (!ok)
                return Result.Failure<ParsedRow>(
                    new Error("Row.TaxIdentifier", "TaxIdentifier format invalid for the given Kind.")
                );
        }

        var fiscalSubjectKind =
            kind == CustomerKind.Individual ? FiscalSubjectKind.Individual : FiscalSubjectKind.Business;

        ParsedSpouse? spouse = null;
        if (kind == CustomerKind.Individual)
        {
            var spouseRes = ParseSpouse(raw, warnings);
            if (spouseRes.IsFailure)
                return Result.Failure<ParsedRow>(spouseRes.Error);
            spouse = spouseRes.Value;
        }

        return Result.Success(
            new ParsedRow(
                kind,
                personalName,
                businessIdentity,
                email,
                phone,
                language,
                dateOfBirth,
                address,
                normalizedTaxId,
                fiscalSubjectKind,
                filingStatus,
                priorYearAgi,
                isReturning,
                raw.OccupationName?.Trim(),
                raw.PrincipalBusinessActivityCode?.Trim(),
                spouse,
                warnings,
                languageProvided,
                isReturningProvided,
                emailOptIn,
                smsOptIn
            )
        );
    }

    private static Result<BusinessIdentity> ParseBusiness(ImportCustomerRow raw, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw.LegalName))
            return Result.Failure<BusinessIdentity>(new Error("Row.LegalName", "LegalName is required for Business."));
        if (!ImportEnum.TryParseName<BusinessStructure>(raw.BusinessStructure, out var structure))
            return Result.Failure<BusinessIdentity>(
                new Error(
                    "Row.BusinessStructure",
                    $"BusinessStructure must be one of {ImportEnum.Names<BusinessStructure>()}. Got '{raw.BusinessStructure}'."
                )
            );

        return BusinessIdentity.Create(
            legalName: raw.LegalName,
            structure: structure,
            dba: raw.Dba,
            formationDate: OptionalDate(raw.FormationDate, "Formation date", warnings)
        );
    }

    // Solo con nombre y apellido. El SSN del cónyuge con forma inválida sí falla la fila; lo demás se omite con aviso.
    private static Result<ParsedSpouse?> ParseSpouse(ImportCustomerRow raw, List<string> warnings)
    {
        var hasFirst = !string.IsNullOrWhiteSpace(raw.SpouseFirstName);
        var hasLast = !string.IsNullOrWhiteSpace(raw.SpouseLastName);
        if (!hasFirst || !hasLast)
        {
            if (hasFirst || hasLast || HasOtherSpouseData(raw))
                warnings.Add("Spouse needs both first and last name; spouse data was not imported.");
            return Result.Success<ParsedSpouse?>(null);
        }

        var normalizedSpouseTaxId = IdentifierNormalizer.NormalizeDigits(raw.SpouseTaxIdentifier);
        if (
            !string.IsNullOrEmpty(normalizedSpouseTaxId)
            && !IdentifierNormalizer.IsValidSsnOrItin(normalizedSpouseTaxId)
        )
            return Result.Failure<ParsedSpouse?>(
                new Error("Row.SpouseTaxIdentifier", "SpouseTaxIdentifier format invalid.")
            );

        var spouseNameRes = PersonalName.Create(raw.SpouseFirstName!, raw.SpouseLastName!, raw.SpouseMiddleName);
        if (spouseNameRes.IsFailure)
        {
            warnings.Add($"Spouse name is not valid ({spouseNameRes.Error.Message}); spouse data was not imported.");
            return Result.Success<ParsedSpouse?>(null);
        }

        EmailAddress? spouseEmail = null;
        if (!string.IsNullOrWhiteSpace(raw.SpouseEmail))
        {
            var emailRes = EmailAddress.Create(raw.SpouseEmail);
            if (emailRes.IsSuccess)
                spouseEmail = emailRes.Value;
            else
                warnings.Add($"Spouse email '{raw.SpouseEmail.Trim()}' is not valid; imported without it.");
        }

        return Result.Success<ParsedSpouse?>(
            new ParsedSpouse(
                spouseNameRes.Value,
                spouseEmail,
                OptionalPhone(raw.SpousePhone, "Spouse phone", warnings),
                PastDate(raw.SpouseDateOfBirth, "Spouse date of birth", warnings),
                normalizedSpouseTaxId
            )
        );
    }

    private static bool HasOtherSpouseData(ImportCustomerRow raw) =>
        new[]
        {
            raw.SpouseMiddleName,
            raw.SpouseDateOfBirth,
            raw.SpouseEmail,
            raw.SpousePhone,
            raw.SpouseTaxIdentifier,
        }.Any(value => !string.IsNullOrWhiteSpace(value));

    // El import normaliza formatos humanos (paréntesis, guiones, sin +1) a E.164 antes del VO estricto.
    private static PhoneNumber? OptionalPhone(string? raw, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var e164 = IdentifierNormalizer.NormalizePhoneToE164(raw);
        var phoneRes = string.IsNullOrEmpty(e164) ? null : PhoneNumber.Create(e164);
        if (phoneRes is { IsSuccess: true })
            return phoneRes.Value;
        warnings.Add($"{label} '{raw.Trim()}' is not valid; imported without it.");
        return null;
    }

    // Al actualizar, el idioma solo cambia si la celda trae uno que se ofrece hoy (En/Es).
    private static (Language Value, bool Provided) OfferedLanguage(string? raw, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (Language.En, false);
        if (!ImportEnum.TryParseName<Language>(raw, out var language))
        {
            warnings.Add($"Language '{raw.Trim()}' is not recognized; imported as En.");
            return (Language.En, false);
        }
        if (!CustomerImportTemplate.OfferedLanguages.Contains(language))
        {
            warnings.Add($"Language '{raw.Trim()}' is no longer offered; imported as En.");
            return (Language.En, false);
        }
        return (language, true);
    }

    private static DateOnly? OptionalDate(string? raw, string label, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;
        warnings.Add($"{label} '{raw.Trim()}' is not a valid date (yyyy-MM-dd); imported without it.");
        return null;
    }

    // Un día de margen por la zona horaria de la oficina.
    private static DateOnly? PastDate(string? raw, string label, List<string> warnings)
    {
        var date = OptionalDate(raw, label, warnings);
        if (date is null || date <= DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1))
            return date;
        warnings.Add($"{label} '{raw!.Trim()}' is in the future; imported without it.");
        return null;
    }

    private static AddressValue? OptionalAddress(ImportCustomerRow raw, List<string> warnings)
    {
        if (
            string.IsNullOrWhiteSpace(raw.AddressLine1)
            && string.IsNullOrWhiteSpace(raw.City)
            && string.IsNullOrWhiteSpace(raw.PostalCode)
        )
            return null;

        var addrRes = AddressValue.Create(
            line1: raw.AddressLine1 ?? string.Empty,
            city: raw.City ?? string.Empty,
            postalCode: raw.PostalCode ?? string.Empty,
            countryCode: string.IsNullOrWhiteSpace(raw.CountryCode) ? "US" : raw.CountryCode,
            line2: raw.AddressLine2,
            region: raw.Region
        );
        if (addrRes.IsSuccess)
            return addrRes.Value;
        warnings.Add($"Address is incomplete or invalid ({addrRes.Error.Message}); imported without it.");
        return null;
    }

    // Acepta "$12,500.50": se quitan el signo de dólar y los espacios.
    private static decimal? OptionalAgi(string? raw, List<string> warnings)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var text = raw.Replace("$", string.Empty).Replace(" ", string.Empty);
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var agi))
            return agi;
        warnings.Add($"Prior year AGI '{raw.Trim()}' is not a number; imported without it.");
        return null;
    }

    // Vacío = no viene (el import decide el valor por defecto); un valor raro se ignora con aviso.
    private static bool? OptIn(string? raw, string label, List<string> warnings)
    {
        var text = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
            return null;
        if (TrueValues.Contains(text))
            return true;
        if (FalseValues.Contains(text))
            return false;
        warnings.Add($"{label} '{raw!.Trim()}' is not recognized (use yes / no); ignored.");
        return null;
    }

    private static (bool Value, bool Provided) ReturningFlag(string? raw, List<string> warnings)
    {
        var text = raw?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(text))
            return (false, false);
        if (TrueValues.Contains(text))
            return (true, true);
        if (FalseValues.Contains(text))
            return (false, true);
        warnings.Add(
            $"Returning customer '{raw!.Trim()}' is not recognized (use true / yes / 1); imported as not returning."
        );
        return (false, false);
    }
}

internal sealed record ParsedRow(
    CustomerKind Kind,
    PersonalName? PersonalName,
    BusinessIdentity? BusinessIdentity,
    EmailAddress? Email,
    PhoneNumber? Phone,
    Language Language,
    DateOnly? DateOfBirth,
    AddressValue? Address,
    string NormalizedTaxIdentifier,
    FiscalSubjectKind FiscalSubjectKind,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    string? OccupationName,
    string? PrincipalBusinessActivityCode,
    ParsedSpouse? Spouse,
    // Datos opcionales que no se pudieron leer y se importan vacíos.
    IReadOnlyList<string> Warnings,
    // Si la celda vino con valor: al actualizar, una celda vacía no borra lo existente.
    bool LanguageProvided = false,
    bool IsReturningCustomerProvided = false,
    // Permisos de contacto del archivo; null = la celda vino vacía.
    bool? EmailOptIn = null,
    bool? SmsOptIn = null
);

internal sealed record ParsedSpouse(
    PersonalName Name,
    EmailAddress? Email,
    PhoneNumber? Phone,
    DateOnly? DateOfBirth,
    string NormalizedTaxIdentifier
);
