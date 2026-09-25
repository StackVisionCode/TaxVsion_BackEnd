using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Application.Requests.Queries.List;

public sealed record ListSignatureRequestsQuery(
    Guid TenantId,
    SignatureRequestStatus? Status,
    string? Category,
    int Page,
    int PageSize,
    // Visibilidad por asignación (P2): el actor y si ve todo (customers.view_all / admin).
    Guid ActorUserId,
    bool CanViewAll,
    // Solo borradores editables (Draft/Ready), para la pestaña "Drafts".
    bool EditableOnly = false
);
