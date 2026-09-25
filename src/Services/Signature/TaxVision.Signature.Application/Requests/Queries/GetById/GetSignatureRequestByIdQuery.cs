namespace TaxVision.Signature.Application.Requests.Queries.GetById;

// ActorUserId + CanViewAll: visibilidad por asignación (P2). Quien no ve todo (customers.view_all / admin) solo
// obtiene el detalle si el cliente de algún firmante está asignado a él; si no, 404 (no filtrar existencia).
public sealed record GetSignatureRequestByIdQuery(
    Guid TenantId,
    Guid SignatureRequestId,
    Guid ActorUserId,
    bool CanViewAll
);
