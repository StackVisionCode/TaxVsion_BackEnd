using ClosedXML.Excel;
using TaxVision.Customer.Application.Imports;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Contrato de la plantilla XLSX (D-8, F5): tres hojas; las columnas son las del lector; cada ejemplo pasa por el
/// lector y el parser sin errores ni avisos; las listas desplegables existen (estrictas, salvo ocupación, que avisa
/// y se empareja en la revisión) y las ocupaciones salen del catálogo.
/// </summary>
public sealed class CustomerImportXlsxTemplateTests
{
    private static readonly string[] Occupations = ["Accountant", "Engineer", "Retired"];

    [Fact]
    public void It_has_the_instructions_clients_and_lists_sheets()
    {
        using var workbook = Open();

        Assert.Equal(["Instructions", "Clients", "Lists"], workbook.Worksheets.Select(s => s.Name));
        Assert.Contains(
            "Overwrite",
            workbook.Worksheet("Instructions").CellsUsed().Select(c => c.GetString()).Aggregate(string.Concat)
        );
    }

    [Fact]
    public void The_client_columns_are_the_ones_the_reader_expects()
    {
        using var workbook = Open();
        var headers = workbook.Worksheet("Clients").Row(1).CellsUsed().Select(c => c.GetString()).ToList();

        Assert.Equal(CustomerImportTemplate.Headers, headers);
    }

    [Fact]
    public async Task Every_example_is_importable_without_warnings()
    {
        var rows = new List<ImportCustomerRow>();
        await foreach (var row in new XlsxCustomerImportReader().ReadAsync(new MemoryStream(Build()), default))
            rows.Add(row);

        Assert.Equal(3, rows.Count);
        Assert.All(
            rows,
            row =>
            {
                var parsed = ImportRowParser.Parse(row);
                Assert.True(parsed.IsSuccess, parsed.IsFailure ? $"Row {row.RowNumber}: {parsed.Error.Message}" : null);
                Assert.Empty(parsed.Value.Warnings);
            }
        );
        Assert.Contains(rows, r => r.Kind == "Individual" && string.IsNullOrEmpty(r.SpouseFirstName));
        Assert.Contains(rows, r => r.Kind == "Individual" && !string.IsNullOrEmpty(r.SpouseFirstName));
        Assert.Contains(rows, r => r.Kind == "Business");
    }

    [Theory]
    [InlineData("Kind", XLErrorStyle.Stop)]
    [InlineData("BusinessStructure", XLErrorStyle.Stop)]
    [InlineData("Language", XLErrorStyle.Stop)]
    [InlineData("FilingStatus", XLErrorStyle.Stop)]
    [InlineData("EmailOptIn", XLErrorStyle.Stop)]
    [InlineData("SmsOptIn", XLErrorStyle.Stop)]
    [InlineData("OccupationName", XLErrorStyle.Warning)]
    public void Each_list_column_has_its_dropdown(string column, XLErrorStyle style)
    {
        using var workbook = Open();
        var sheet = workbook.Worksheet("Clients");
        var index = CustomerImportTemplate.Headers.ToList().IndexOf(column) + 1;

        var validation = sheet.DataValidations.Single(v => v.Ranges.Any(r => r.Contains(sheet.Cell(5, index))));

        Assert.Equal(XLAllowedValues.List, validation.AllowedValues);
        Assert.Equal(style, validation.ErrorStyle);
    }

    [Fact]
    public void The_occupations_come_from_the_catalog()
    {
        using var workbook = Open();
        var values = workbook.Worksheet("Lists").CellsUsed().Select(c => c.GetString()).ToHashSet();

        Assert.Superset(Occupations.ToHashSet(), values);
        Assert.Contains("Married Filing Jointly", values);
    }

    [Fact]
    public void The_language_list_has_only_the_offered_languages()
    {
        using var workbook = Open();
        var values = workbook.Worksheet("Lists").CellsUsed().Select(c => c.GetString()).ToHashSet();

        Assert.Contains("En", values);
        Assert.Contains("Es", values);
        Assert.DoesNotContain("Pt", values);
        Assert.DoesNotContain("Fr", values);
    }

    private static byte[] Build() => XlsxCustomerImportTemplate.Build(Occupations);

    private static XLWorkbook Open() => new(new MemoryStream(Build()));
}
