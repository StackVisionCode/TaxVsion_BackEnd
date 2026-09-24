using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public sealed record ListSignatureRequestsQuery(
    Guid TenantId,
    SignatureRequestStatus? Status,
    string? Category,
    int Page,
    int PageSize,
    // Solo borradores editables (Draft/Ready), para la pestaña "Drafts".
    bool EditableOnly = false
);
