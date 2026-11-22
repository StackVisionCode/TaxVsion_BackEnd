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
    bool EditableOnly = false,
    // Solo solicitudes con un firmante mapeado a este cliente (pestaña Signatures del perfil).
    Guid? CustomerId = null,
    // En curso que vencen dentro de 48 h: la misma regla que expiringWithin48h del resumen.
    bool ExpiringSoon = false,
    // Las del actor que esperan a los firmantes: la regla "waiting" de GET /signature/work/{userId}.
    bool MineOnly = false,
    // Instante de referencia de ExpiringSoon; null = ahora.
    DateTime? NowUtc = null
);
