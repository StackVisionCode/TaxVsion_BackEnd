using Microsoft.EntityFrameworkCore;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.Infrastructure.Persistence;

namespace TaxVision.Tasks.Infrastructure.ClientForms.Persistence.Repositories;

// Regla única de visibilidad de la bandeja, componible en SQL: la usan el listado y las métricas.
internal static class FormRequestInboxVisibility
{
    // Con CanViewAll se ve todo el tenant. Si no, responsable y solicitante solo ven por sí mismos las
    // externas; las de cliente piden asignación al cliente.
    internal static IQueryable<FormRequest> VisibleInInbox(
        this IQueryable<FormRequest> rows,
        TasksDbContext context,
        Guid tenantId,
        Guid actorUserId,
        bool canViewAll
    ) =>
        canViewAll
            ? rows
            : rows.Where(item =>
                (
                    item.ParticipantKind == FormParticipantKind.External
                    && (item.ResponsibleUserId == actorUserId || item.RequestedByUserId == actorUserId)
                )
                || (
                    item.CustomerId.HasValue
                    && context
                        .CustomerAssignmentProjections.IgnoreQueryFilters()
                        .Any(assignment =>
                            assignment.TenantId == tenantId
                            && assignment.CustomerId == item.CustomerId.Value
                            && assignment.UserId == actorUserId
                        )
                )
            );
}
