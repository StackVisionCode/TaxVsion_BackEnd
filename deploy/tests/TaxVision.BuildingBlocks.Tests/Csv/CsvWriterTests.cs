using System.Text;
using BuildingBlocks.Web.Csv;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Csv;

/// <summary>
/// BB-17. Los tres defectos que se corrigieron son de los que solo se ven en producción: el CSV
/// salía con LF (los contenedores corren Linux, y <c>AppendLine</c> usa <c>Environment.NewLine</c>)
/// y sin BOM, así que Excel lo abría con la codepage ANSI y los acentos de los nombres de clientes
/// aparecían rotos.
/// </summary>
public sealed class CsvWriterTests
{
    private static readonly string[] Headers = ["Id", "Nombre", "Monto"];

    [Fact]
    public void Write_TerminaCadaLineaEnCrlf_NoEnElSeparadorDelSistema()
    {
        var csv = CsvWriter.Write(
            Headers,
            [
                ["1", "Ana", "100"],
            ]
        );

        Assert.Equal("Id,Nombre,Monto\r\n1,Ana,100\r\n", csv);
        Assert.DoesNotContain("\n\n", csv);
    }

    [Fact]
    public void WriteWithBom_EmpiezaConElBomUtf8()
    {
        // Encoding.UTF8.GetBytes() NO emite BOM — solo GetPreamble() lo hace. Ese fue el bug.
        var bytes = CsvWriter.WriteWithBom(
            Headers,
            [
                ["1", "Ana", "100"],
            ]
        );

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
    }

    [Fact]
    public void WriteWithBom_ConservaLosAcentosYLaEñe()
    {
        var bytes = CsvWriter.WriteWithBom(
            Headers,
            [
                ["1", "Muñoz Peña, José", "100"],
            ]
        );

        var decoded = new UTF8Encoding(false).GetString(bytes.Skip(3).ToArray());
        Assert.Contains("Muñoz Peña, José", decoded);
    }

    [Theory]
    [InlineData("con,coma", "\"con,coma\"")]
    [InlineData("con\"comilla", "\"con\"\"comilla\"")]
    [InlineData("con\nsalto", "\"con\nsalto\"")]
    [InlineData("limpio", "limpio")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Escape_AplicaLasReglasDeRfc4180(string? value, string expected)
    {
        var csv = CsvWriter.Write(
            ["H"],
            [
                [value],
            ]
        );

        Assert.Equal($"H\r\n{expected}\r\n", csv);
    }

    [Fact]
    public void WriteTo_ProduceExactamenteLoMismoQueWrite()
    {
        // Es el camino de streaming: si divergiera, un reporte grande saldría distinto al chico.
        IEnumerable<IReadOnlyList<string?>> rows =
        [
            ["1", "Ana", "100"],
            ["2", "Luis, Jr.", "200"],
        ];
        var builder = new StringBuilder();

        CsvWriter.WriteTo(builder, Headers, rows);

        Assert.Equal(CsvWriter.Write(Headers, rows), builder.ToString());
    }

    [Fact]
    public void Write_SinFilas_DejaSoloElHeader()
    {
        Assert.Equal("Id,Nombre,Monto\r\n", CsvWriter.Write(Headers, []));
    }
}
