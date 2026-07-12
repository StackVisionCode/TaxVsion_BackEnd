using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public static class ListSignatureRequestsHandler
{
    public static Task<ListSignatureRequestsResult> Handle(
        ListSignatureRequestsQuery query,
        ISignatureRequestReadService readService,
        CancellationToken ct
    ) => readService.ListAsync(query, ct);
}
