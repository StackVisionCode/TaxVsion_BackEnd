using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Library.Queries;

public sealed record ExportFormTemplateQuery(Guid TemplateId);

// Exporta el borrador de una plantilla de plataforma, para respaldar el EmbeddedResource en el repo.
public static class ExportFormTemplateHandler
{
    public static async Task<Result<string>> Handle(
        ExportFormTemplateQuery query,
        IFormTemplateRepository templates,
        CancellationToken ct
    )
    {
        var found = await templates.GetByIdAsync(PlatformTenant.Id, query.TemplateId, ct);
        return found.IsFailure ? Result.Failure<string>(found.Error) : Result.Success(found.Value.Draft.Json);
    }
}
