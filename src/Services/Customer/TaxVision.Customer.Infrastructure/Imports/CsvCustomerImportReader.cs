using System.Globalization;
using System.Runtime.CompilerServices;
using CsvHelper;
using CsvHelper.Configuration;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Lee filas de CSV en modo stream (sin cargar todo el archivo). Tolerante a columnas extra.
/// Headers case-insensitive; columnas omitidas se interpretan como null.
/// </summary>
internal sealed class CsvCustomerImportReader : ICustomerImportReader
{
    public ImportSourceKind SourceKind => ImportSourceKind.Csv;

    public async IAsyncEnumerable<ImportCustomerRow> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
            PrepareHeaderForMatch = args => args.Header.Trim(),
            MissingFieldFound = null,
            HeaderValidated = null,
            BadDataFound = null,
            TrimOptions = TrimOptions.Trim,
            Delimiter = ",",
        };

        using var reader = new StreamReader(stream, leaveOpen: false);
        using var csv = new CsvReader(reader, config);

        // Leer header
        await csv.ReadAsync();
        csv.ReadHeader();

        int rowNumber = 1;
        while (await csv.ReadAsync())
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;

            yield return new ImportCustomerRow
            {
                RowNumber = rowNumber,
                Kind = GetField(csv, "Kind"),
                FirstName = GetField(csv, "FirstName"),
                MiddleName = GetField(csv, "MiddleName"),
                LastName = GetField(csv, "LastName"),
                Prefix = GetField(csv, "Prefix"),
                Suffix = GetField(csv, "Suffix"),
                LegalName = GetField(csv, "LegalName"),
                Dba = GetField(csv, "Dba"),
                BusinessStructure = GetField(csv, "BusinessStructure"),
                FormationDate = GetField(csv, "FormationDate"),
                PrincipalBusinessActivityCode = GetField(csv, "PrincipalBusinessActivityCode"),
                DateOfBirth = GetField(csv, "DateOfBirth"),
                OccupationName = GetField(csv, "OccupationName"),
                Email = GetField(csv, "Email"),
                Phone = GetField(csv, "Phone"),
                Language = GetField(csv, "Language"),
                PreferredChannel = GetField(csv, "PreferredChannel"),
                AddressLine1 = GetField(csv, "AddressLine1"),
                AddressLine2 = GetField(csv, "AddressLine2"),
                City = GetField(csv, "City"),
                Region = GetField(csv, "Region"),
                PostalCode = GetField(csv, "PostalCode"),
                CountryCode = GetField(csv, "CountryCode"),
                TaxIdentifier = GetField(csv, "TaxIdentifier"),
                FilingStatus = GetField(csv, "FilingStatus"),
                PriorYearAgi = GetField(csv, "PriorYearAgi"),
                IsReturningCustomer = GetField(csv, "IsReturningCustomer"),
                SpouseFirstName = GetField(csv, "SpouseFirstName"),
                SpouseMiddleName = GetField(csv, "SpouseMiddleName"),
                SpouseLastName = GetField(csv, "SpouseLastName"),
                SpouseDateOfBirth = GetField(csv, "SpouseDateOfBirth"),
                SpouseEmail = GetField(csv, "SpouseEmail"),
                SpousePhone = GetField(csv, "SpousePhone"),
                SpouseTaxIdentifier = GetField(csv, "SpouseTaxIdentifier"),
            };
        }
    }

    private static string? GetField(CsvReader csv, string name)
    {
        try
        {
            var value = csv.GetField(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            // Columna no presente en el header
            return null;
        }
    }
}
