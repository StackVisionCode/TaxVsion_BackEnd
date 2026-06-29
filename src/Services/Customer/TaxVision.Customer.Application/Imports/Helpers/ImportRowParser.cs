using System.Globalization;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Application.Imports.Helpers;

/// <summary>
/// Convierte la fila string-pura del archivo en value objects + enums validados.
/// Si algo falla, devuelve un Result.Failure con el codigo del primer error.
/// El handler del worker lo usa para validar antes de tocar la BD.
/// </summary>
internal static class ImportRowParser
{
    public static Result<ParsedRow> Parse(ImportCustomerRow raw)
    {
        // Kind
        if (!Enum.TryParse<CustomerKind>(raw.Kind, ignoreCase: true, out var kind))
            return Result.Failure<ParsedRow>(
                new Error("Row.Kind", $"Kind must be Individual or Business. Got '{raw.Kind}'.")
            );

        // Language/Channel con defaults sanos
        var language = Enum.TryParse<Language>(raw.Language, ignoreCase: true, out var lang) ? lang : Language.En;
        var channel = Enum.TryParse<PreferredChannel>(raw.PreferredChannel, ignoreCase: true, out var ch)
            ? ch
            : PreferredChannel.Email;

        // Email obligatorio (Customer.Register lo exige)
        if (string.IsNullOrWhiteSpace(raw.Email))
            return Result.Failure<ParsedRow>(new Error("Row.Email", "Email is required."));

        var emailRes = EmailAddress.Create(raw.Email);
        if (emailRes.IsFailure)
            return Result.Failure<ParsedRow>(emailRes.Error);

        PhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(raw.Phone))
        {
            // El import normaliza formatos humanos (parentesis, guiones, sin +1) a E.164
            // antes de pasar al VO estricto.
            var e164 = IdentifierNormalizer.NormalizePhoneToE164(raw.Phone);
            if (string.IsNullOrEmpty(e164))
                return Result.Failure<ParsedRow>(
                    new Error("Row.Phone", $"Phone '{raw.Phone}' cannot be normalized to E.164.")
                );
            var phoneRes = PhoneNumber.Create(e164);
            if (phoneRes.IsFailure)
                return Result.Failure<ParsedRow>(phoneRes.Error);
            phone = phoneRes.Value;
        }

        // PersonalName (Individual) o BusinessIdentity (Business)
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
            if (string.IsNullOrWhiteSpace(raw.LegalName))
                return Result.Failure<ParsedRow>(new Error("Row.LegalName", "LegalName is required for Business."));
            if (!Enum.TryParse<BusinessStructure>(raw.BusinessStructure, ignoreCase: true, out var structure))
                return Result.Failure<ParsedRow>(
                    new Error(
                        "Row.BusinessStructure",
                        $"BusinessStructure must be one of Sole/Llc/Partnership/SCorp/CCorp/NonProfit/Other. Got '{raw.BusinessStructure}'."
                    )
                );

            DateOnly? formationDate = null;
            if (!string.IsNullOrWhiteSpace(raw.FormationDate))
            {
                if (!DateOnly.TryParse(raw.FormationDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                    return Result.Failure<ParsedRow>(
                        new Error("Row.FormationDate", $"FormationDate must be yyyy-MM-dd. Got '{raw.FormationDate}'.")
                    );
                formationDate = d;
            }

            var bizRes = BusinessIdentity.Create(
                legalName: raw.LegalName,
                structure: structure,
                dba: raw.Dba,
                formationDate: formationDate
            );
            if (bizRes.IsFailure)
                return Result.Failure<ParsedRow>(bizRes.Error);
            businessIdentity = bizRes.Value;
        }

        DateOnly? dateOfBirth = null;
        if (!string.IsNullOrWhiteSpace(raw.DateOfBirth))
        {
            if (!DateOnly.TryParse(raw.DateOfBirth, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob))
                return Result.Failure<ParsedRow>(
                    new Error("Row.DateOfBirth", $"DateOfBirth must be yyyy-MM-dd. Got '{raw.DateOfBirth}'.")
                );
            dateOfBirth = dob;
        }

        // Direccion primaria (opcional)
        AddressValue? address = null;
        if (
            !string.IsNullOrWhiteSpace(raw.AddressLine1)
            || !string.IsNullOrWhiteSpace(raw.City)
            || !string.IsNullOrWhiteSpace(raw.PostalCode)
        )
        {
            var addrRes = AddressValue.Create(
                line1: raw.AddressLine1 ?? string.Empty,
                city: raw.City ?? string.Empty,
                postalCode: raw.PostalCode ?? string.Empty,
                countryCode: string.IsNullOrWhiteSpace(raw.CountryCode) ? "US" : raw.CountryCode,
                line2: raw.AddressLine2,
                region: raw.Region
            );
            if (addrRes.IsFailure)
                return Result.Failure<ParsedRow>(addrRes.Error);
            address = addrRes.Value;
        }

        // Fiscal: SubjectKind, FilingStatus, PriorYearAgi, IsReturningCustomer
        FilingStatus? filingStatus = null;
        if (!string.IsNullOrWhiteSpace(raw.FilingStatus))
        {
            if (!Enum.TryParse<FilingStatus>(raw.FilingStatus, ignoreCase: true, out var fs))
                return Result.Failure<ParsedRow>(
                    new Error("Row.FilingStatus", $"FilingStatus invalid: '{raw.FilingStatus}'.")
                );
            filingStatus = fs;
        }

        decimal? priorYearAgi = null;
        if (!string.IsNullOrWhiteSpace(raw.PriorYearAgi))
        {
            if (!decimal.TryParse(raw.PriorYearAgi, NumberStyles.Number, CultureInfo.InvariantCulture, out var agi))
                return Result.Failure<ParsedRow>(
                    new Error("Row.PriorYearAgi", $"PriorYearAgi must be a decimal. Got '{raw.PriorYearAgi}'.")
                );
            priorYearAgi = agi;
        }

        var isReturning =
            !string.IsNullOrWhiteSpace(raw.IsReturningCustomer)
            && (
                raw.IsReturningCustomer.Equals("true", StringComparison.OrdinalIgnoreCase)
                || raw.IsReturningCustomer.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || raw.IsReturningCustomer == "1"
            );

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

        // Spouse (opcional, solo Individual)
        ParsedSpouse? spouse = null;
        if (
            kind == CustomerKind.Individual
            && !string.IsNullOrWhiteSpace(raw.SpouseFirstName)
            && !string.IsNullOrWhiteSpace(raw.SpouseLastName)
        )
        {
            var spouseNameRes = PersonalName.Create(raw.SpouseFirstName, raw.SpouseLastName, raw.SpouseMiddleName);
            if (spouseNameRes.IsFailure)
                return Result.Failure<ParsedRow>(spouseNameRes.Error);

            EmailAddress? spouseEmail = null;
            if (!string.IsNullOrWhiteSpace(raw.SpouseEmail))
            {
                var r = EmailAddress.Create(raw.SpouseEmail);
                if (r.IsFailure)
                    return Result.Failure<ParsedRow>(r.Error);
                spouseEmail = r.Value;
            }

            PhoneNumber? spousePhone = null;
            if (!string.IsNullOrWhiteSpace(raw.SpousePhone))
            {
                var e164 = IdentifierNormalizer.NormalizePhoneToE164(raw.SpousePhone);
                if (string.IsNullOrEmpty(e164))
                    return Result.Failure<ParsedRow>(
                        new Error("Row.SpousePhone", $"SpousePhone '{raw.SpousePhone}' cannot be normalized to E.164.")
                    );
                var r = PhoneNumber.Create(e164);
                if (r.IsFailure)
                    return Result.Failure<ParsedRow>(r.Error);
                spousePhone = r.Value;
            }

            DateOnly? spouseDob = null;
            if (!string.IsNullOrWhiteSpace(raw.SpouseDateOfBirth))
            {
                if (
                    !DateOnly.TryParse(
                        raw.SpouseDateOfBirth,
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.None,
                        out var sd
                    )
                )
                    return Result.Failure<ParsedRow>(
                        new Error("Row.SpouseDateOfBirth", $"SpouseDateOfBirth must be yyyy-MM-dd.")
                    );
                spouseDob = sd;
            }

            var normalizedSpouseTaxId = IdentifierNormalizer.NormalizeDigits(raw.SpouseTaxIdentifier);
            if (
                !string.IsNullOrEmpty(normalizedSpouseTaxId)
                && !IdentifierNormalizer.IsValidSsnOrItin(normalizedSpouseTaxId)
            )
                return Result.Failure<ParsedRow>(
                    new Error("Row.SpouseTaxIdentifier", "SpouseTaxIdentifier format invalid.")
                );

            spouse = new ParsedSpouse(spouseNameRes.Value, spouseEmail, spousePhone, spouseDob, normalizedSpouseTaxId);
        }

        return Result.Success(
            new ParsedRow(
                kind,
                personalName,
                businessIdentity,
                emailRes.Value,
                phone,
                language,
                channel,
                dateOfBirth,
                address,
                normalizedTaxId,
                fiscalSubjectKind,
                filingStatus,
                priorYearAgi,
                isReturning,
                raw.OccupationName?.Trim(),
                raw.PrincipalBusinessActivityCode?.Trim(),
                spouse
            )
        );
    }
}

internal sealed record ParsedRow(
    CustomerKind Kind,
    PersonalName? PersonalName,
    BusinessIdentity? BusinessIdentity,
    EmailAddress Email,
    PhoneNumber? Phone,
    Language Language,
    PreferredChannel PreferredChannel,
    DateOnly? DateOfBirth,
    AddressValue? Address,
    string NormalizedTaxIdentifier,
    FiscalSubjectKind FiscalSubjectKind,
    FilingStatus? FilingStatus,
    decimal? PriorYearAgi,
    bool IsReturningCustomer,
    string? OccupationName,
    string? PrincipalBusinessActivityCode,
    ParsedSpouse? Spouse
);

internal sealed record ParsedSpouse(
    PersonalName Name,
    EmailAddress? Email,
    PhoneNumber? Phone,
    DateOnly? DateOfBirth,
    string NormalizedTaxIdentifier
);
