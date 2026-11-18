using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.FiscalProfiles;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Estricto en identidad (tipo, nombre, email, forma del SSN); tolerante en lo opcional: un valor que no sirve se
/// importa vacío y deja un aviso. Las listas se leen por nombre o sinónimo, nunca por número.
/// </summary>
public sealed class ImportRowParserTests
{
    [Theory]
    [InlineData("1")]
    [InlineData("7")]
    public void A_number_is_not_a_client_type(string kind)
    {
        var result = ImportRowParser.Parse(Individual(kind: kind));

        Assert.Equal("Row.Kind", result.Error.Code);
    }

    [Fact]
    public void A_number_is_not_a_filing_status_and_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(filingStatus: "7"));

        Assert.Null(result.Value.FilingStatus);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Filing status"));
    }

    [Theory]
    [InlineData("Pt")]
    [InlineData("fr")]
    public void A_language_no_longer_offered_is_imported_as_english_with_a_warning(string language)
    {
        var result = ImportRowParser.Parse(Individual(language: language));

        Assert.Equal(Language.En, result.Value.Language);
        Assert.False(result.Value.LanguageProvided);
        Assert.Contains(result.Value.Warnings, w => w.Contains("no longer offered"));
    }

    [Fact]
    public void A_date_of_birth_in_the_future_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(dateOfBirth: "2099-01-01"));

        Assert.Null(result.Value.DateOfBirth);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Date of birth") && w.Contains("future"));
    }

    [Fact]
    public void A_row_the_reader_could_not_split_fails_with_the_reason()
    {
        var row = Individual();
        row = new ImportCustomerRow
        {
            RowNumber = row.RowNumber,
            Kind = row.Kind,
            FirstName = row.FirstName,
            LastName = row.LastName,
            Email = row.Email,
            ReadError = "Unclosed quote.",
        };

        var result = ImportRowParser.Parse(row);

        Assert.Equal("Row.UnclosedQuote", result.Error.Code);
    }

    [Fact]
    public void A_number_is_not_a_business_structure()
    {
        var result = ImportRowParser.Parse(Business("2"));

        Assert.Equal("Row.BusinessStructure", result.Error.Code);
    }

    [Fact]
    public void The_business_structure_error_lists_the_exact_accepted_names()
    {
        var result = ImportRowParser.Parse(Business("Sole"));

        Assert.Contains("SoleProprietorship", result.Error.Message);
        Assert.DoesNotContain("Sole/", result.Error.Message);
    }

    [Fact]
    public void Names_are_still_accepted_in_any_case()
    {
        var result = ImportRowParser.Parse(Individual(kind: "individual", filingStatus: "marriedjoint"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Empty(result.Value.Warnings);
    }

    [Theory]
    [InlineData("Married Filing Jointly", FilingStatus.MarriedJoint)]
    [InlineData("MFJ", FilingStatus.MarriedJoint)]
    [InlineData("mfs", FilingStatus.MarriedSeparate)]
    [InlineData("HOH", FilingStatus.HeadOfHousehold)]
    [InlineData("Qualifying Widow(er)", FilingStatus.QualifyingSurvivingSpouse)]
    [InlineData("QSS", FilingStatus.QualifyingSurvivingSpouse)]
    [InlineData("Soltero", FilingStatus.Single)]
    public void Common_filing_status_names_are_accepted(string raw, FilingStatus expected)
    {
        var result = ImportRowParser.Parse(Individual(filingStatus: raw));

        Assert.Equal(expected, result.Value.FilingStatus);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public void An_invalid_phone_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(phone: "12"));

        Assert.Null(result.Value.Phone);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Phone"));
    }

    [Fact]
    public void An_invalid_date_of_birth_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(dateOfBirth: "31/02/1990"));

        Assert.Null(result.Value.DateOfBirth);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Date of birth"));
    }

    [Fact]
    public void An_incomplete_address_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(addressLine1: "1 Main St"));

        Assert.Null(result.Value.Address);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Address"));
    }

    [Fact]
    public void An_unknown_language_is_imported_as_english_and_does_not_count_as_provided()
    {
        var result = ImportRowParser.Parse(Individual(language: "Klingon"));

        Assert.Equal(Language.En, result.Value.Language);
        Assert.False(result.Value.LanguageProvided);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Language"));
    }

    [Fact]
    public void An_invalid_prior_year_agi_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Individual(priorYearAgi: "about 50k"));

        Assert.Null(result.Value.PriorYearAgi);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Prior year AGI"));
    }

    [Fact]
    public void A_prior_year_agi_with_dollar_sign_and_commas_is_read()
    {
        var result = ImportRowParser.Parse(Individual(priorYearAgi: "$12,500.50"));

        Assert.Equal(12500.50m, result.Value.PriorYearAgi);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public void An_unclear_returning_flag_counts_as_not_returning_and_warns()
    {
        var result = ImportRowParser.Parse(Individual(isReturning: "maybe"));

        Assert.False(result.Value.IsReturningCustomer);
        Assert.False(result.Value.IsReturningCustomerProvided);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Returning"));
    }

    [Fact]
    public void An_invalid_formation_date_is_imported_without_it()
    {
        var result = ImportRowParser.Parse(Business("LLC", formationDate: "soon"));

        Assert.Null(result.Value.BusinessIdentity!.FormationDate);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Formation date"));
    }

    [Fact]
    public void A_spouse_without_last_name_is_ignored_with_a_warning()
    {
        var result = ImportRowParser.Parse(Individual(spouseFirstName: "Sam"));

        Assert.Null(result.Value.Spouse);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Spouse"));
    }

    [Fact]
    public void An_invalid_spouse_email_keeps_the_spouse_without_it()
    {
        var result = ImportRowParser.Parse(
            Individual(spouseFirstName: "Sam", spouseLastName: "Lovelace", spouseEmail: "not-an-email")
        );

        Assert.NotNull(result.Value.Spouse);
        Assert.Null(result.Value.Spouse!.Email);
        Assert.Contains(result.Value.Warnings, w => w.Contains("Spouse email"));
    }

    [Fact]
    public void An_invalid_spouse_tax_id_still_fails_the_row()
    {
        var result = ImportRowParser.Parse(
            Individual(spouseFirstName: "Sam", spouseLastName: "Lovelace", spouseTaxIdentifier: "000-12-3456")
        );

        Assert.Equal("Row.SpouseTaxIdentifier", result.Error.Code);
    }

    [Fact]
    public void An_invalid_tax_id_still_fails_the_row()
    {
        var result = ImportRowParser.Parse(Individual(taxIdentifier: "000-12-3456"));

        Assert.Equal("Row.TaxIdentifier", result.Error.Code);
    }

    [Fact]
    public void Contact_opt_in_columns_are_read()
    {
        var result = ImportRowParser.Parse(Individual(emailOptIn: "yes", smsOptIn: "No"));

        Assert.True(result.Value.EmailOptIn);
        Assert.False(result.Value.SmsOptIn);
        Assert.Empty(result.Value.Warnings);
    }

    [Fact]
    public void Empty_opt_in_columns_are_not_provided()
    {
        var result = ImportRowParser.Parse(Individual());

        Assert.Null(result.Value.EmailOptIn);
        Assert.Null(result.Value.SmsOptIn);
    }

    [Fact]
    public void An_unclear_opt_in_is_ignored_with_a_warning()
    {
        var result = ImportRowParser.Parse(Individual(smsOptIn: "maybe"));

        Assert.Null(result.Value.SmsOptIn);
        Assert.Contains(result.Value.Warnings, w => w.Contains("SMS opt-in"));
    }

    // D-3: sin email la fila sigue; la regla de contacto mínimo la decide el agregado al crear.
    [Fact]
    public void A_missing_email_is_read_as_no_email()
    {
        var result = ImportRowParser.Parse(Individual(email: "", phone: "+13055551234"));

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Null(result.Value.Email);
    }

    [Fact]
    public void An_invalid_email_still_fails_the_row()
    {
        var result = ImportRowParser.Parse(Individual(email: "not-an-email"));

        Assert.StartsWith("Email.", result.Error.Code);
    }

    private static ImportCustomerRow Individual(
        string kind = "Individual",
        string? filingStatus = null,
        string email = "ada@example.com",
        string? phone = null,
        string? dateOfBirth = null,
        string? addressLine1 = null,
        string? language = null,
        string? priorYearAgi = null,
        string? isReturning = null,
        string? taxIdentifier = null,
        string? spouseFirstName = null,
        string? spouseLastName = null,
        string? spouseEmail = null,
        string? spouseTaxIdentifier = null,
        string? emailOptIn = null,
        string? smsOptIn = null
    ) =>
        new()
        {
            RowNumber = 2,
            Kind = kind,
            FirstName = "Ada",
            LastName = "Lovelace",
            Email = email,
            FilingStatus = filingStatus,
            Phone = phone,
            DateOfBirth = dateOfBirth,
            AddressLine1 = addressLine1,
            Language = language,
            PriorYearAgi = priorYearAgi,
            IsReturningCustomer = isReturning,
            TaxIdentifier = taxIdentifier,
            SpouseFirstName = spouseFirstName,
            SpouseLastName = spouseLastName,
            SpouseEmail = spouseEmail,
            SpouseTaxIdentifier = spouseTaxIdentifier,
            EmailOptIn = emailOptIn,
            SmsOptIn = smsOptIn,
        };

    private static ImportCustomerRow Business(string structure, string? formationDate = null) =>
        new()
        {
            RowNumber = 3,
            Kind = "Business",
            LegalName = "Acme LLC",
            BusinessStructure = structure,
            FormationDate = formationDate,
            Email = "contact@acme.com",
        };
}
