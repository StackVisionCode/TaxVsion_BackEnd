using BuildingBlocks.Platform;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.Content;
using PdfSharp.Pdf.Content.Objects;
using PdfSharp.Pdf.IO;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Infrastructure.Sealing;

namespace TaxVision.Signature.Tests.Sealing;

/// <summary>
/// Lo que queda en el PDF: iniciales, fecha y texto llevan solo su contenido (sin cajas), a la letra que
/// eligió el preparador o a la automática (75 % del alto, acotada) y solo se achican si no caben; la firma
/// lleva marco solo si la oficina lo tiene activo. Se lee el contenido real de la página sellada.
/// </summary>
public sealed class FieldStampingTests
{
    // Página carta: 612 × 792 pt. Una caja de 0.0253 de alto ≈ 20 pt.
    private const double TwentyPointsHigh = 20.0 / 792;

    public FieldStampingTests()
    {
        if (GlobalFontSettings.FontResolver is null)
            GlobalFontSettings.FontResolver = new SealingFontResolver();
    }

    [Fact]
    public void A_date_in_auto_takes_three_quarters_of_the_box_capped_by_the_platform_and_has_no_box()
    {
        var stamped = Stamp(Field(SignatureFieldKind.Date, height: TwentyPointsHigh));

        // 20 pt × 0.75 = 15 → tope automático de 12 pt.
        Assert.Contains(12.0, stamped.FontSizes);
        Assert.Equal(0, stamped.Shapes);
    }

    [Fact]
    public void A_fixed_size_is_honored_and_never_grows_to_fill_the_box()
    {
        var stamped = Stamp(Field(SignatureFieldKind.Initials, height: 60.0 / 792, fontSizePt: 8));

        Assert.Contains(8.0, stamped.FontSizes);
        Assert.Equal(0, stamped.Shapes);
    }

    [Fact]
    public void A_fixed_size_that_does_not_fit_shrinks_until_it_does()
    {
        var stamped = Stamp(Field(SignatureFieldKind.Date, height: TwentyPointsHigh, width: 0.06, fontSizePt: 30));

        var size = Assert.Single(stamped.FontSizes, size => size != FooterFontSize);
        Assert.True(size < 30, $"expected the date to shrink, got {size} pt");
    }

    [Fact]
    public void Long_text_wraps_and_shrinks_to_fit_the_box_height()
    {
        var text = string.Join(' ', Enumerable.Repeat("additional income from rental property", 6));
        var stamped = Stamp(
            Field(SignatureFieldKind.Text, height: 40.0 / 792, width: 0.3, fontSizePt: 14, value: text)
        );

        Assert.All(stamped.FontSizes.Where(size => size != FooterFontSize), size => Assert.True(size < 14));
        Assert.Equal(0, stamped.Shapes);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void The_signature_frame_follows_the_office_setting(bool showFrame, bool expectFrame)
    {
        var stamped = Stamp(Field(SignatureFieldKind.Signature, height: 50.0 / 792), showFrame);

        Assert.Equal(expectFrame, stamped.Shapes > 0);
    }

    [Fact]
    public void Initials_use_the_bold_italic_face_shown_in_the_editor()
    {
        var pdf = Seal([Field(SignatureFieldKind.Initials, height: TwentyPointsHigh)], showFrame: true);

        Assert.Contains(FontNames(pdf), name => name.Contains("BoldItalic", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void A_checkbox_is_just_a_drawn_check_mark()
    {
        var stamped = Stamp(Field(SignatureFieldKind.Checkbox, height: TwentyPointsHigh, width: 20.0 / 612));

        Assert.True(stamped.Lines > 0);
        Assert.Equal(0, stamped.Shapes);
    }

    // ------------------------------------------------------------------

    private const double FooterFontSize = 6;

    private static SealedFieldRender Field(
        SignatureFieldKind kind,
        double height,
        double width = 0.25,
        int? fontSizePt = null,
        string? value = null
    ) =>
        new(
            Page: 1,
            X: 0.1,
            Y: 0.2,
            Width: width,
            Height: height,
            Kind: kind,
            Label: null,
            SignerDisplayName: "Ana Maria Perez",
            SignedAtUtc: new DateTime(2026, 10, 4, 5, 23, 0, DateTimeKind.Utc),
            Value: value,
            FontSizePt: fontSizePt
        );

    private sealed record StampedContent(IReadOnlyList<double> FontSizes, int Shapes, int Lines);

    /// <summary>Sella una página en blanco con un campo y devuelve lo que agregó el campo (sin el pie de auditoría).</summary>
    private static StampedContent Stamp(SealedFieldRender field, bool showFrame = true)
    {
        var baseline = Operators(Seal([], showFrame));
        var withField = Operators(Seal([field], showFrame));
        int Added(string op) => withField.Count(o => o.Name == op) - baseline.Count(o => o.Name == op);

        var fontSizes = withField.Where(o => o.Name == "Tf").Select(o => SizeOf(o.Operands[1])).ToList();
        // Sacar los del pie (presentes también en la línea base).
        foreach (var footer in baseline.Where(o => o.Name == "Tf").Select(o => SizeOf(o.Operands[1])))
            fontSizes.Remove(footer);

        // Rectángulos y curvas = cajas/bordes; "l" = trazos rectos (el ✓ de la casilla).
        return new StampedContent(fontSizes, Added("re") + Added("c"), Added("l"));
    }

    private static byte[] Seal(IReadOnlyList<SealedFieldRender> fields, bool showFrame)
    {
        var engine = new PdfSharpSealingEngine(new FixedPlatformSettings());
        var style = new StampStyle(showFrame, "MM/dd/yyyy", AutoFontMinPt: 6, AutoFontMaxPt: 12);
        return engine
            .Seal(new SealingRequest(BlankLetterPage(), fields, new string('a', 64), "footer", style))
            .SealedPdfBytes;
    }

    private static byte[] BlankLetterPage()
    {
        using var document = new PdfDocument();
        var page = document.AddPage();
        page.Width = PdfSharp.Drawing.XUnit.FromPoint(612);
        page.Height = PdfSharp.Drawing.XUnit.FromPoint(792);
        using var stream = new MemoryStream();
        document.Save(stream, closeStream: false);
        return stream.ToArray();
    }

    private static IEnumerable<string> FontNames(byte[] pdfBytes)
    {
        using var stream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        var fonts = document.Pages[0].Resources.Elements.GetDictionary("/Font");
        return fonts is null
            ? []
            : fonts
                .Elements.Values.Select(item =>
                    (item as PdfSharp.Pdf.Advanced.PdfReference)?.Value as PdfDictionary ?? item as PdfDictionary
                )
                .Select(font => font?.Elements.GetName("/BaseFont") ?? string.Empty)
                .ToList();
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

    private static double SizeOf(CObject operand) =>
        operand switch
        {
            CReal real => Math.Round(real.Value, 1),
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
