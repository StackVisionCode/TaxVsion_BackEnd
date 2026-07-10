using TaxVision.Signature.Application.Abstractions;

namespace TaxVision.Signature.Application.Requests.Queries.GetById;

public static class GetSignatureRequestByIdHandler
{
    public static async Task<SignatureRequestResponse?> Handle(
        GetSignatureRequestByIdQuery query,
        ISignatureRequestRepository repository,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(query.TenantId, query.SignatureRequestId, ct);
        return request is null ? null : SignatureRequestResponse.From(request);
    }
}
