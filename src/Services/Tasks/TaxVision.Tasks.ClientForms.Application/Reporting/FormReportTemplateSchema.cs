using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public sealed record FormReportTemplateInfo(
    string Title,
    IReadOnlyList<FormReportableKey> Keys,
    FormReportLabels Labels
);

/// <summary>
/// Resuelve el título y las claves reportables de la última versión publicada del template, y las
/// etiquetas de las opciones de todas sus versiones (una fila del índice puede venir de cualquiera).
/// </summary>
public static class FormReportTemplateSchema
{
    public static async Task<FormReportTemplateInfo?> LatestAsync(
        Guid tenantId,
        Guid templateId,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        CancellationToken ct
    )
    {
        var all = await versions.ListAsync(tenantId, templateId, ct);
        var ordered = all.OrderByDescending(version => version.VersionNumber).ToArray();
        if (ordered.Length == 0)
            return null;
        var parsed = ordered
            .Select(version => (version.VersionNumber, Definition: engine.ParseDefinition(version.Definition.Json)))
            .Where(item => item.Definition.IsSuccess)
            .Select(item => (item.VersionNumber, item.Definition.Value))
            .ToArray();
        var labels = FormReportLabels.From(parsed);
        var latest = ordered[0];
        var keys =
            parsed.Length > 0 && parsed[0].VersionNumber == latest.VersionNumber
                ? FormReportSchema.ReportableKeys(parsed[0].Value)
                : [];
        return new FormReportTemplateInfo(latest.Title, keys, labels);
    }
}
