using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Domain.Requests;

namespace TaxVision.Signature.Infrastructure.Persistence.Queries;

/// <summary>Recorte por asignación del listado, compartido con el resumen para que no diverjan.</summary>
internal static class SignatureRequestVisibility
{
    // Borrador propio, o algún firmante mapeado a un cliente asignado al actor. La request no tiene CustomerId:
    // se atraviesa Signers.MappedCustomerId. Tenant explícito en el subquery (scope de Wolverine sin tenant ambiental).
    // Semi-join que parte de los clientes del actor (T-1) e IX_Signers_MappedCustomerId: no recorre todos los firmantes.
    // Las dos ramas van en un UNION ALL de ids y no en un OR, que SQL Server evalúa fila a fila.
    public static IQueryable<SignatureRequest> VisibleTo(
        this IQueryable<SignatureRequest> requests,
        SignatureDbContext db,
        Guid tenantId,
        Guid assignee
    )
    {
        var ownDrafts = db
            .SignatureRequests.IgnoreQueryFilters()
            .Where(r =>
                r.TenantId == tenantId && r.Status == SignatureRequestStatus.Draft && r.CreatedByUserId == assignee
            )
            .Select(r => r.Id);
        var ofMyClients =
            from a in db.Set<CustomerAssignmentProjection>().IgnoreQueryFilters()
            where a.TenantId == tenantId && a.UserId == assignee
            join s in db.Signers on (Guid?)a.CustomerId equals s.MappedCustomerId
            select s.SignatureRequestId;
        var visibleIds = ownDrafts.Concat(ofMyClients);
        return requests.Where(r => visibleIds.Contains(r.Id));
    }
}
