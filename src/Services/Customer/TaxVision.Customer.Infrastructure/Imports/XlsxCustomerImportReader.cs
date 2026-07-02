using System.Runtime.CompilerServices;
using ClosedXML.Excel;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Infrastructure.Imports;

/// <summary>
/// Lee filas de XLSX (sheet 1). Headers en fila 1. Datos desde fila 2.
/// ClosedXML carga el workbook completo en memoria; el guard de tamano del POST limita a 10MB.
/// </summary>
internal sealed class XlsxCustomerImportReader : ICustomerImportReader
{
    public ImportSourceKind SourceKind => ImportSourceKind.Xlsx;

    public async IAsyncEnumerable<ImportCustomerRow> ReadAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken ct
    )
    {
        using var workbook = new XLWorkbook(stream);
        var sheet =
            workbook.Worksheets.FirstOrDefault() ?? throw new InvalidOperationException("XLSX has no worksheets.");

        // Mapear headers de fila 1 a indices de columna
        var headerRow = sheet.Row(1);
        var headers = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var name = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(name) && !headers.ContainsKey(name))
                headers[name] = cell.Address.ColumnNumber;
        }

        var dataRows = sheet.RowsUsed().Skip(1);
        int rowNumber = 1;

        foreach (var row in dataRows)
        {
            ct.ThrowIfCancellationRequested();
            rowNumber++;

            yield return new ImportCustomerRow
            {
                RowNumber = rowNumber,
                Kind = GetCell(row, headers, "Kind"),
                FirstName = GetCell(row, headers, "FirstName"),
                MiddleName = GetCell(row, headers, "MiddleName"),
                LastName = GetCell(row, headers, "LastName"),
                Prefix = GetCell(row, headers, "Prefix"),
                Suffix = GetCell(row, headers, "Suffix"),
                LegalName = GetCell(row, headers, "LegalName"),
                Dba = GetCell(row, headers, "Dba"),
                BusinessStructure = GetCell(row, headers, "BusinessStructure"),
                FormationDate = GetCell(row, headers, "FormationDate"),
                PrincipalBusinessActivityCode = GetCell(row, headers, "PrincipalBusinessActivityCode"),
                DateOfBirth = GetCell(row, headers, "DateOfBirth"),
                OccupationName = GetCell(row, headers, "OccupationName"),
                Email = GetCell(row, headers, "Email"),
                Phone = GetCell(row, headers, "Phone"),
                Language = GetCell(row, headers, "Language"),
                PreferredChannel = GetCell(row, headers, "PreferredChannel"),
                AddressLine1 = GetCell(row, headers, "AddressLine1"),
                AddressLine2 = GetCell(row, headers, "AddressLine2"),
                City = GetCell(row, headers, "City"),
                Region = GetCell(row, headers, "Region"),
                PostalCode = GetCell(row, headers, "PostalCode"),
                CountryCode = GetCell(row, headers, "CountryCode"),
                TaxIdentifier = GetCell(row, headers, "TaxIdentifier"),
                FilingStatus = GetCell(row, headers, "FilingStatus"),
                PriorYearAgi = GetCell(row, headers, "PriorYearAgi"),
                IsReturningCustomer = GetCell(row, headers, "IsReturningCustomer"),
                SpouseFirstName = GetCell(row, headers, "SpouseFirstName"),
                SpouseMiddleName = GetCell(row, headers, "SpouseMiddleName"),
                SpouseLastName = GetCell(row, headers, "SpouseLastName"),
                SpouseDateOfBirth = GetCell(row, headers, "SpouseDateOfBirth"),
                SpouseEmail = GetCell(row, headers, "SpouseEmail"),
                SpousePhone = GetCell(row, headers, "SpousePhone"),
                SpouseTaxIdentifier = GetCell(row, headers, "SpouseTaxIdentifier"),
            };
        }

        await Task.CompletedTask;
    }

    private static string? GetCell(IXLRow row, Dictionary<string, int> headers, string name)
    {
        if (!headers.TryGetValue(name, out var col))
            return null;
        var cell = row.Cell(col);
        if (cell is null || cell.IsEmpty())
            return null;

        // Para fechas/numeros, usar la representacion string que ClosedXML genera
        var value = cell.GetFormattedString();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
