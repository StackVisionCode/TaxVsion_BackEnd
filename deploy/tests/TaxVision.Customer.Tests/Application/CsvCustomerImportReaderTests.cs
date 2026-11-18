using System.Text;
using TaxVision.Customer.Application.Imports.Dtos;
using TaxVision.Customer.Infrastructure.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// Una comilla sin cerrar hace que el lector junte el resto del archivo en una sola fila: esa fila lleva el motivo
/// para que falle con un mensaje claro. Una celda entre comillas con salto de línea es válida y no se marca.
/// </summary>
public sealed class CsvCustomerImportReaderTests
{
    [Fact]
    public async Task A_row_with_an_unclosed_quote_carries_the_reason()
    {
        var rows = await ReadAsync(
            "Kind,FirstName,LastName,Email\n"
                + "Individual,Ana,One,ana@example.com\n"
                + "Individual,\"Broken,Two,bo@example.com\n"
                + "Individual,Cy,Three,cy@example.com\n"
        );

        Assert.Null(rows[0].ReadError);
        Assert.NotNull(rows[^1].ReadError);
    }

    [Fact]
    public async Task A_quoted_cell_with_a_line_break_is_valid()
    {
        var rows = await ReadAsync(
            "Kind,FirstName,LastName,AddressLine1\nIndividual,Ana,One,\"Apt 4\nMain St\"\nIndividual,Bo,Two,\"Say \"\"hi\"\"\"\n"
        );

        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.Null(row.ReadError));
    }

    private static async Task<List<ImportCustomerRow>> ReadAsync(string csv)
    {
        var rows = new List<ImportCustomerRow>();
        await foreach (
            var row in new CsvCustomerImportReader().ReadAsync(new MemoryStream(Encoding.UTF8.GetBytes(csv)), default)
        )
            rows.Add(row);
        return rows;
    }
}
