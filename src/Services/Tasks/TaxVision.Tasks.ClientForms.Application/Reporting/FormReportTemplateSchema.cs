using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

public sealed record FormReportTemplateInfo(string Title, IReadOnlyList<FormReportableKey> Keys);

/// <summary>Resuelve el título y las claves reportables de la última versión publicada del template.</summary>
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
        var latest = all.OrderByDescending(version => version.VersionNumber).FirstOrDefault();
        if (latest is null)
            return null;
        var definition = engine.ParseDefinition(latest.Definition.Json);
        if (definition.IsFailure)
            return new FormReportTemplateInfo(latest.Title, []);
        return new FormReportTemplateInfo(latest.Title, FormReportSchema.ReportableKeys(definition.Value));
    }
}
