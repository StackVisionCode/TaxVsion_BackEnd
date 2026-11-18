using TaxVision.Customer.Application.Imports;

namespace TaxVision.Customer.Tests.Application;

/// <summary>
/// El reporte del import se abre en Excel: un nombre que empieza como fórmula (=, +, -, @, tab, retorno) se
/// escribe como texto para que Excel no lo ejecute. Las comillas se escapan y los saltos de línea se aplanan.
/// </summary>
public sealed class ImportReportCsvTests
{
    [Theory]
    [InlineData(
        "=HYPERLINK(\"http://evil.test\",\"click\")",
        "\"'=HYPERLINK(\"\"http://evil.test\"\",\"\"click\"\")\""
    )]
    [InlineData("+1+2", "\"'+1+2\"")]
    [InlineData("-2+3", "\"'-2+3\"")]
    [InlineData("@SUM(A1)", "\"'@SUM(A1)\"")]
    [InlineData("\tcmd", "\"'\tcmd\"")]
    public void A_value_that_starts_like_a_formula_is_written_as_text(string value, string expected)
    {
        Assert.Equal(expected, ImportReportCsv.Cell(value));
    }

    [Theory]
    [InlineData("Ana Torres", "\"Ana Torres\"")]
    [InlineData("Say \"hi\"", "\"Say \"\"hi\"\"\"")]
    [InlineData("line\nbreak", "\"line break\"")]
    [InlineData(null, "\"\"")]
    public void A_normal_value_is_quoted_and_escaped(string? value, string expected)
    {
        Assert.Equal(expected, ImportReportCsv.Cell(value));
    }
}
