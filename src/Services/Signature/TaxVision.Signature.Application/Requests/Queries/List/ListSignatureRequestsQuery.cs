using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public sealed record ListSignatureRequestsQuery(
    Guid TenantId,
    SignatureRequestStatus? Status,
    SignatureCategory? Category,
    int Page,
    int PageSize
);
