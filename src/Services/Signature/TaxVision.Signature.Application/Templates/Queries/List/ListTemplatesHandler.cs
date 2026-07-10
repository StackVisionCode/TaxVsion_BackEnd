using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Templates.Queries.List;

public static class ListTemplatesHandler
{
    public static Task<ListTemplatesResult> Handle(
        ListTemplatesQuery query,
        ISignatureTemplateReadService readService,
        CancellationToken ct
    ) => readService.ListAsync(query, ct);
}
