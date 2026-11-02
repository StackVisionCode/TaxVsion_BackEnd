using PdfSharp.Pdf;
using TaxVision.Signature.Application.Documents;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Infrastructure.Validation;

namespace TaxVision.Signature.Tests.Validation;

/// <summary>
/// El inspector solo extrae hechos (PDF real, páginas, firmas previas); los límites del tenant los aplica
/// <see cref="DocumentAcceptancePolicy"/>. Así el mismo PDF puede servir a oficinas con topes distintos.
/// </summary>
public sealed class PdfSharpPdfInspectorTests
{
    private readonly PdfSharpPdfInspector _inspector = new();

    [Fact]
    public void A_real_pdf_reports_its_page_count_and_no_structural_issues()
    {
        var inspection = _inspector.Inspect(PdfWithPages(3), "application/pdf");

        Assert.Empty(inspection.StructuralIssues);
        Assert.Equal(3, inspection.PageCount);
        Assert.False(inspection.HasExistingSignatures);
        Assert.Equal(64, inspection.ContentSha256.Length);
    }

    [Fact]
    public void A_file_renamed_to_pdf_is_caught_by_its_header()
    {
        var inspection = _inspector.Inspect("PK\u0003\u0004 not a pdf"u8.ToArray(), "application/pdf");

        Assert.Equal("Signature.DocumentValidation.MagicHeader", Assert.Single(inspection.StructuralIssues).Code);
        Assert.Null(inspection.PageCount);
    }

    [Theory]
    [InlineData("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n")]
    [InlineData("%PDF-1.7\n")]
    public void A_pdf_the_parser_cannot_open_is_reported_as_damaged_instead_of_throwing(string content)
    {
        var inspection = _inspector.Inspect(System.Text.Encoding.ASCII.GetBytes(content), "application/pdf");

        Assert.Equal("Signature.DocumentValidation.Corrupted", Assert.Single(inspection.StructuralIssues).Code);
        Assert.Null(inspection.PageCount);
    }

    [Fact]
    public void A_non_pdf_content_type_is_rejected()
    {
        var inspection = _inspector.Inspect(PdfWithPages(1), "image/png");

        Assert.Equal("Signature.DocumentValidation.MimeType", Assert.Single(inspection.StructuralIssues).Code);
    }

    [Fact]
    public void The_page_limit_comes_from_the_tenant_not_from_the_inspector()
    {
        var inspection = _inspector.Inspect(PdfWithPages(3), "application/pdf");

        var strict = DocumentAcceptancePolicy.Combine(inspection, DocumentLimits.Default().WithMaxPages(2).Value);
        var relaxed = DocumentAcceptancePolicy.Combine(inspection, DocumentLimits.Default());

        Assert.False(strict.IsAcceptable);
        Assert.Equal("Signature.DocumentValidation.TooManyPages", Assert.Single(strict.Issues).Code);
        Assert.True(relaxed.IsAcceptable);
    }

    [Fact]
    public void The_size_limit_comes_from_the_tenant()
    {
        var issues = DocumentAcceptancePolicy.Evaluate(
            sizeBytes: 3 * 1024 * 1024,
            pageCount: 1,
            hasExistingSignatures: false,
            DocumentLimits.Default().WithMaxPdfBytes(2 * 1024 * 1024).Value
        );

        var issue = Assert.Single(issues);
        Assert.Equal("Signature.DocumentValidation.SizeTooLarge", issue.Code);
        Assert.Contains("2 MB", issue.Message);
    }

    private static byte[] PdfWithPages(int pages)
    {
        using var document = new PdfDocument();
        for (var i = 0; i < pages; i++)
            document.AddPage();
        using var output = new MemoryStream();
        document.Save(output, closeStream: false);
        return output.ToArray();
    }
}
