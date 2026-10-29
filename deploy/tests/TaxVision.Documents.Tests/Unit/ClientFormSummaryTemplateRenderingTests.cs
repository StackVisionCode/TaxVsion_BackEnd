using System.Globalization;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Generations.ClientFormSummary;
using TaxVision.Documents.Infrastructure.Rendering;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>Renderiza client_form.submission_summary.v1 con Fluid real: logo vs. nombre y caja de firma
/// en las mismas medidas que el ancla que recibe Signature.</summary>
public sealed class ClientFormSummaryTemplateRenderingTests
{
    private static IReadOnlyDictionary<string, object?> SampleData(string logo, bool signatureBox) =>
        new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object>
            {
                ["title"] = "Organizer 2026",
                ["customerName"] = "Ada Lovelace",
                ["versionNumber"] = 1,
                ["revisionNumber"] = 1,
                ["submittedAt"] = "2026-10-28 23:07 UTC",
                ["completedAt"] = "2026-10-28 23:09 UTC",
                ["generatedOn"] = "2026-10-28 23:09 UTC",
                ["pages"] = new List<object>(),
                ["attachments"] = new List<object>(),
                ["hasAttachments"] = false,
                ["logo"] = logo,
                ["brandColor"] = "#0074d4",
                ["displayName"] = "Office Name",
                ["showSignatureBox"] = signatureBox,
            },
        };

    private static async Task<string> RenderAsync(string logo, bool signatureBox)
    {
        var renderer = new TemplateDocumentRenderer(NullLogger<TemplateDocumentRenderer>.Instance);
        var result = await renderer.RenderHtmlAsync(
            ClientFormSubmissionSummaryRequestedConsumer.TemplateKey,
            1,
            Guid.Empty,
            SampleData(logo, signatureBox)
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    [Fact]
    public async Task Shows_the_logo_instead_of_the_name_when_there_is_one()
    {
        var html = await RenderAsync("data:image/png;base64,AAAA", signatureBox: false);

        Assert.Contains("src=\"data:image/png;base64,AAAA\"", html);
        Assert.DoesNotContain("<div class=\"wordmark\">", html);
    }

    [Fact]
    public async Task Falls_back_to_the_name_without_a_logo()
    {
        var html = await RenderAsync("", signatureBox: false);

        Assert.Contains("<div class=\"wordmark\">Office Name</div>", html);
        Assert.DoesNotContain("class=\"sign-page\"", html);
    }

    [Fact]
    public async Task Signature_box_uses_the_anchor_measurements()
    {
        var html = await RenderAsync("", signatureBox: true);

        static string Mm(double value) => value.ToString(CultureInfo.InvariantCulture) + "mm";
        Assert.Contains("class=\"sign-page\"", html);
        Assert.Contains($"top: {Mm(ClientFormSignatureAnchor.BoxTopMm)}", html);
        Assert.Contains($"width: {Mm(ClientFormSignatureAnchor.BoxWidthMm)}", html);
        Assert.Contains($"height: {Mm(ClientFormSignatureAnchor.BoxHeightMm)}", html);
    }
}
