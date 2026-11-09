using System.Globalization;
using System.Text;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;

namespace TaxVision.Documents.Application.Generations.ClientFormSummary;

/// <summary>Arma el diccionario de render del Submission Summary y el nombre del archivo, sin I/O.</summary>
public static class ClientFormSummaryRenderModelBuilder
{
    private const string DefaultBrandColor = "#0074d4";
    private const string DateTimeFormat = "yyyy-MM-dd HH:mm 'UTC'";

    public static IReadOnlyDictionary<string, object?> Build(
        SubmissionSummarySnapshot snapshot,
        BrandingPayload? branding,
        string platformBrand,
        bool includeSignatureBox,
        DateTime nowUtc
    )
    {
        var culture = CultureInfo.InvariantCulture;
        var logo = BrandingRenderValues.EmbeddedLogo(branding?.LogoDataUri);
        var brandColor = BrandingRenderValues.IsHexColor(branding?.BrandColorHex)
            ? branding!.BrandColorHex!
            : DefaultBrandColor;
        var displayName = string.IsNullOrWhiteSpace(branding?.DisplayName) ? platformBrand : branding!.DisplayName!;

        var pages = snapshot
            .Pages.Select(
                object (page) =>
                    new Dictionary<string, object>
                    {
                        ["title"] = page.Title ?? string.Empty,
                        ["sections"] = page
                            .Sections.Select(
                                object (section) =>
                                    new Dictionary<string, object>
                                    {
                                        ["title"] = section.Title ?? string.Empty,
                                        ["items"] = ItemsToList(section.Items),
                                        ["rows"] = section
                                            .Rows.Select(
                                                object (row) =>
                                                    new Dictionary<string, object>
                                                    {
                                                        ["label"] = row.Label,
                                                        ["items"] = ItemsToList(row.Items),
                                                    }
                                            )
                                            .ToList(),
                                    }
                            )
                            .ToList(),
                    }
            )
            .ToList();

        var attachments = snapshot
            .Attachments.Select(
                object (a) => new Dictionary<string, object> { ["label"] = a.Label, ["fileName"] = a.FileName }
            )
            .ToList();

        var generatedOn = (snapshot.CompletedAtUtc ?? nowUtc).ToString(DateTimeFormat, culture);

        return new Dictionary<string, object?>
        {
            ["summary"] = new Dictionary<string, object>
            {
                ["title"] = snapshot.Title,
                ["customerName"] = snapshot.CustomerDisplayName ?? string.Empty,
                ["versionNumber"] = snapshot.VersionNumber,
                ["revisionNumber"] = snapshot.RevisionNumber,
                ["submittedAt"] = snapshot.SubmittedAtUtc?.ToString(DateTimeFormat, culture) ?? string.Empty,
                ["completedAt"] = snapshot.CompletedAtUtc?.ToString(DateTimeFormat, culture) ?? string.Empty,
                ["generatedOn"] = generatedOn,
                ["pages"] = pages,
                ["attachments"] = attachments,
                ["hasAttachments"] = attachments.Count > 0,
                ["logo"] = logo,
                ["brandColor"] = brandColor,
                ["displayName"] = displayName,
                ["showSignatureBox"] = includeSignatureBox,
            },
        };
    }

    // Lista fija y no Path.GetInvalidFileNameChars(): en Linux esa solo trae '/' y el archivo se baja en Windows.
    private const string UnsafeFileNameChars = "<>:\"/\\|?*";

    // "{Title} - Submission summary (rev {N}).pdf", saneado para nombre de archivo.
    public static string FileName(SubmissionSummarySnapshot snapshot)
    {
        var title = string.IsNullOrWhiteSpace(snapshot.Title) ? "Form" : snapshot.Title.Trim();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title)
            sb.Append(char.IsControl(c) || UnsafeFileNameChars.Contains(c) ? '_' : c);
        var safe = sb.ToString().Trim();
        if (safe.Length > 120)
            safe = safe[..120].Trim();
        return $"{safe} - Submission summary (rev {snapshot.RevisionNumber}).pdf";
    }

    private static List<object> ItemsToList(IReadOnlyList<SubmissionSummaryItem> items) =>
        items
            .Select(object (i) => new Dictionary<string, object> { ["label"] = i.Label, ["value"] = i.Value })
            .ToList();
}
