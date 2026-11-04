using BuildingBlocks.Platform;
using PdfSharp.Fonts;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Infrastructure.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// Cabecera del Certificate of Completion: el título no puede montarse sobre el logo. Se mide en la
/// página real (posición de la imagen y de la línea base del título de 27 pt).
/// </summary>
public sealed class CertificateHeaderTests
{
    private const double TitleSize = 27;

    public CertificateHeaderTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new SealingFontResolver();
    }

    [Fact]
    public void The_title_starts_below_the_logo_with_clear_space()
    {
        var pdf = new PdfSharpCertificateRenderer(new FixedPlatformSettings())
            .Render(
                new CertificateOfCompletionModel(
                    Guid.NewGuid(),
                    "F8 header",
                    "Fiscal",
                    DateTime.UtcNow.AddHours(-1),
                    DateTime.UtcNow,
                    [],
                    []
                )
            )
            .CertificatePdfBytes;

        var operators = Operators(pdf);
        // PDF: origen abajo; la imagen se dibuja con "cm" [w 0 0 h x y] → su borde inferior es y.
        var logoBottom = operators
            .Select((op, i) => (op, i))
            .Where(t => t.op.Name == "Do")
            .Select(t => operators.Take(t.i).Last(o => o.Name == "cm"))
            .Select(cm => Number(cm.Operands[5]))
            .Max();
        var titleBaseline = TitleBaseline(operators);

        // Ascendente de Times ≈ 0.9 em: la parte de arriba del título queda bajo el logo con aire.
        var titleTop = titleBaseline + TitleSize * 0.9;
        Assert.True(logoBottom - titleTop >= 8, $"logo bottom {logoBottom:0.0}, title top {titleTop:0.0}");
    }

    /// <summary>Línea base del título: "Td" es relativo dentro de BT, "Tm" fija la posición absoluta.</summary>
    private static double TitleBaseline(List<COperator> operators)
    {
        var y = 0.0;
        var inTitle = false;
        foreach (var op in operators)
        {
            switch (op.Name)
            {
                case "BT":
                    y = 0;
                    break;
                case "Tf":
                    inTitle = Math.Abs(Number(op.Operands[1]) - TitleSize) < 0.01;
                    break;
                case "Td" or "TD":
                    y += Number(op.Operands[1]);
                    break;
                case "Tm":
                    y = Number(op.Operands[5]);
                    break;
                case "Tj" or "TJ" when inTitle:
                    return y;
            }
        }
        throw new InvalidOperationException("title not found");
    }

    private static List<COperator> Operators(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var operators = new List<COperator>();
        Collect(ContentReader.ReadContent(document.Pages[0]), operators);
        return operators;
    }

    private static void Collect(CSequence sequence, List<COperator> into)
    {
        foreach (var item in sequence)
        {
            if (item is COperator op)
                into.Add(op);
            else if (item is CSequence nested)
                Collect(nested, into);
        }
    }

    private static double Number(CObject operand) =>
        operand switch
        {
            CReal real => real.Value,
            CInteger integer => integer.Value,
            _ => double.NaN,
        };

    private sealed class FixedPlatformSettings : IPlatformSettings
    {
        public PlatformSettingsSnapshot Current { get; } =
            new(
                1,
                "TaxOffix",
                "TaxOffix LLC",
                "taxoffix.io",
                "api.taxoffix.io",
                "support@taxoffix.io",
                "billing@taxoffix.io"
            );
    }
}
