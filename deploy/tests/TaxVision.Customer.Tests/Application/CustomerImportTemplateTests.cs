using System.Text;
using TaxVision.Customer.Application.Imports;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Application.Imports.Helpers;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Contrato de la plantilla oficial: cada fila de ejemplo tiene tantos valores como columnas, el lector la entiende
/// y cada ejemplo pasa la validación del import. Si alguien cambia una columna, esta prueba se pone roja.
/// </summary>
public sealed class CustomerImportTemplateTests
{
    [Fact]
    public void Every_example_row_has_one_value_per_column()
    {
        var lines = CustomerImportTemplate.Csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var columns = lines[0].Split(',').Length;

        Assert.All(lines.Skip(1), line => Assert.Equal(columns, line.Split(',').Length));
    }

    [Fact]
    public async Task Every_example_row_is_importable()
    {
        var rows = await ReadAsync();

        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            row =>
            {
                var parsed = ImportRowParser.Parse(row);
                Assert.True(parsed.IsSuccess, parsed.IsFailure ? $"Row {row.RowNumber}: {parsed.Error.Message}" : null);
                Assert.Empty(parsed.Value.Warnings);
            }
        );
    }

    [Fact]
    public async Task The_examples_cover_an_individual_with_spouse_and_a_business()
    {
        var rows = await ReadAsync();

        Assert.Contains(rows, row => row.Kind == "Individual" && string.IsNullOrEmpty(row.SpouseFirstName));
        Assert.Contains(rows, row => row.Kind == "Individual" && !string.IsNullOrEmpty(row.SpouseFirstName));
        Assert.Contains(rows, row => row.Kind == "Business");
    }

    [Fact]
    public async Task The_template_shows_the_contact_opt_in_columns()
    {
        var rows = await ReadAsync();

        Assert.Contains(rows, row => !string.IsNullOrEmpty(row.EmailOptIn) && !string.IsNullOrEmpty(row.SmsOptIn));
    }

    private static async Task<List<ImportCustomerRow>> ReadAsync()
    {
        var reader = new CsvCustomerImportReader();
        var rows = new List<ImportCustomerRow>();
        await foreach (
            var row in reader.ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(CustomerImportTemplate.Csv)), default)
        )
            rows.Add(row);
        return rows;
    }
}
