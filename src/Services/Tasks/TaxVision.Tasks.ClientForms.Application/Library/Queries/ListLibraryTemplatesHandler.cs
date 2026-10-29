using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Library.Queries;

public sealed record ListLibraryTemplatesQuery;

public static class ListLibraryTemplatesHandler
{
    public static async Task<IReadOnlyList<LibraryTemplateSummaryResponse>> Handle(
        ListLibraryTemplatesQuery query,
        IFormLibraryRepository library,
        CancellationToken ct
    )
    {
        var records = await library.ListAsync(ct);
        return
        [
            .. records.Select(record => new LibraryTemplateSummaryResponse(
                record.LibraryKey,
                record.Name,
                record.Description,
                record.Purpose,
                record.VersionNumber
            )),
        ];
    }
}
