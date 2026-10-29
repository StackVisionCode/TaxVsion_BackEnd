using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Library.Queries;

public sealed record GetLibraryTemplateQuery(string LibraryKey);

public static class GetLibraryTemplateHandler
{
    public static async Task<Result<LibraryTemplatePreviewResponse>> Handle(
        GetLibraryTemplateQuery query,
        IFormLibraryRepository library,
        CancellationToken ct
    )
    {
        var detail = await library.GetByKeyAsync(query.LibraryKey, ct);
        if (detail is null)
            return Result.Failure<LibraryTemplatePreviewResponse>(FormLibraryErrors.NotFound);
        return Result.Success(
            new LibraryTemplatePreviewResponse(
                detail.LibraryKey,
                detail.Name,
                detail.Description,
                detail.Purpose,
                detail.VersionNumber,
                detail.DefinitionJson
            )
        );
    }
}
