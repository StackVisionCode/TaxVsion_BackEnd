namespace TaxVision.Customer.Application.Imports;

/// <summary>
/// Plantilla oficial del import: columnas y ejemplos, los mismos en el CSV y en el XLSX. Las columnas son las que lee
/// el import; una prueba de contrato comprueba que cada ejemplo tenga un valor por columna y pase la validación sin
/// avisos. EmailOptIn/SmsOptIn vacíos: email sí, SMS no.
/// </summary>
public static class CustomerImportTemplate
{
    public const string FileName = "customer-import-template.csv";
    public const string XlsxFileName = "customer-import-template.xlsx";

    // Idiomas que se ofrecen hoy; Pt y Fr solo quedan en clientes que ya los tenían.
    public static readonly IReadOnlyList<Domain.Customers.Language> OfferedLanguages =
    [
        Domain.Customers.Language.En,
        Domain.Customers.Language.Es,
    ];

    public static readonly IReadOnlyList<string> Headers =
    [
        "Kind",
        "FirstName",
        "MiddleName",
        "LastName",
        "Prefix",
        "Suffix",
        "LegalName",
        "Dba",
        "BusinessStructure",
        "FormationDate",
        "PrincipalBusinessActivityCode",
        "DateOfBirth",
        "OccupationName",
        "Email",
        "Phone",
        "Language",
        "AddressLine1",
        "AddressLine2",
        "City",
        "Region",
        "PostalCode",
        "CountryCode",
        "TaxIdentifier",
        "FilingStatus",
        "PriorYearAgi",
        "IsReturningCustomer",
        "SpouseFirstName",
        "SpouseMiddleName",
        "SpouseLastName",
        "SpouseDateOfBirth",
        "SpouseEmail",
        "SpousePhone",
        "SpouseTaxIdentifier",
        "EmailOptIn",
        "SmsOptIn",
    ];

    // Teléfonos en E.164. El import también acepta formatos US comunes como (305) 555-1234.
    // Individual soltero, individual casado con cónyuge y una LLC.
    public static readonly IReadOnlyList<IReadOnlyList<string>> Examples =
    [
        Row(
            "Individual,Maria,,Lopez,Ms,,,,,,,1990-06-01,Accountant,maria.lopez@example.com,"
                + "+13055559876,Es,77 Ocean Dr,,Miami,FL,33139,US,123-45-6780,Single,38000.00,"
                + "true,,,,,,,,yes,no"
        ),
        Row(
            "Individual,John,A,Doe,Mr,,,,,,,1985-03-15,Accountant,john.doe@example.com,"
                + "+13055551234,En,123 Main St,,Miami,FL,33101,US,123-45-6789,MarriedJoint,45000.00,"
                + "false,Jane,B,Doe,1987-07-22,jane.doe@example.com,+13057654321,987-65-4321,yes,yes"
        ),
        Row(
            "Business,,,,,,Acme LLC,Acme Inc,Llc,2020-01-15,541211,,"
                + ",contact@acme.com,+13055551111,En,500 Brickell Ave,Suite 200,Miami,FL,33131,US,"
                + "12-3456789,,,,,,,,,,,yes,no"
        ),
    ];

    public static readonly string Csv =
        string.Join('\n', [string.Join(',', Headers), .. Examples.Select(example => string.Join(',', example))]) + "\n";

    private static string[] Row(string csv) => csv.Split(',');
}
