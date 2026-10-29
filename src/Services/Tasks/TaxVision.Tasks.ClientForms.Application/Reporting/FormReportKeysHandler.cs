using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Reporting;

// Claves reportables del template (de su última versión) para poblar el filtro del CRM.
public static class FormReportKeysHandler
{
    public static async Task<Result<FormReportKeysResponse>> Handle(
        FormReportKeysQuery query,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        CancellationToken ct
    )
    {
        var latest = await FormReportTemplateSchema.LatestAsync(query.TenantId, query.TemplateId, versions, engine, ct);
        if (latest is null)
            return Result.Failure<FormReportKeysResponse>(FormTemplateErrors.NotFound);
        return Result.Success(new FormReportKeysResponse(query.TemplateId, latest.Keys));
    }
}
