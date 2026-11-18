using System.Text;
using ClosedXML.Excel;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// F4-1: el CSV limpio del CRM lleva <c>SourceRow</c> (la fila del archivo original). Si viene, es el número de la
/// fila en resultados, reporte y avisos, igual que en la revisión. Sin la columna, o con un valor que no avanza, se
/// numera como siempre, para que el número nunca se repita.
/// </summary>
public sealed class CustomerImportReaderRowNumberTests
{
    [Fact]
    public async Task A_csv_with_source_rows_keeps_the_original_numbers()
    {
        var rows = await ReadCsvAsync(
            "SourceRow,Kind,FirstName,LastName\n2,Individual,Ana,One\n5,Individual,Bo,Two\n9,Individual,Cy,Three\n"
        );

        Assert.Equal([2, 5, 9], rows.Select(r => r.RowNumber));
    }

    [Fact]
    public async Task A_csv_without_source_rows_is_numbered_by_line()
    {
        var rows = await ReadCsvAsync("Kind,FirstName,LastName\nIndividual,Ana,One\nIndividual,Bo,Two\n");

        Assert.Equal([2, 3], rows.Select(r => r.RowNumber));
    }

    [Fact]
    public async Task A_source_row_that_does_not_move_forward_is_ignored()
    {
        var rows = await ReadCsvAsync(
            "SourceRow,Kind,FirstName,LastName\n4,Individual,Ana,One\n4,Individual,Bo,Two\nx,Individual,Cy,Three\n"
        );

        Assert.Equal([4, 5, 6], rows.Select(r => r.RowNumber));
    }

    [Fact]
    public async Task An_xlsx_with_source_rows_keeps_the_original_numbers()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Clients");
        sheet.Cell(1, 1).Value = "SourceRow";
        sheet.Cell(1, 2).Value = "Kind";
        sheet.Cell(1, 3).Value = "FirstName";
        sheet.Cell(1, 4).Value = "LastName";
        sheet.Cell(2, 1).Value = 3;
        sheet.Cell(2, 2).Value = "Individual";
        sheet.Cell(2, 3).Value = "Ana";
        sheet.Cell(2, 4).Value = "One";
        sheet.Cell(3, 1).Value = 7;
        sheet.Cell(3, 2).Value = "Individual";
        sheet.Cell(3, 3).Value = "Bo";
        sheet.Cell(3, 4).Value = "Two";
        var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = await ReadAsync(new XlsxCustomerImportReader(), stream);

        Assert.Equal([3, 7], rows.Select(r => r.RowNumber));
    }

    private static Task<List<ImportCustomerRow>> ReadCsvAsync(string csv) =>
        ReadAsync(new CsvCustomerImportReader(), new MemoryStream(Encoding.UTF8.GetBytes(csv)));

    private static async Task<List<ImportCustomerRow>> ReadAsync(ICustomerImportReader reader, Stream stream)
    {
        var rows = new List<ImportCustomerRow>();
        await foreach (var row in reader.ReadAsync(stream, default))
            rows.Add(row);
        return rows;
    }
}
