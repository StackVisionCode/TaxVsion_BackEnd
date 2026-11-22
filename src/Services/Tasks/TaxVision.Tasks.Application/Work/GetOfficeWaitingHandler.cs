using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;

namespace TaxVision.Tasks.Application.Work;

/// <summary>Lo que espera al cliente en toda la oficina, por tipo (mismos tipos que el proveedor de trabajo).</summary>
public sealed record OfficeWaitingCount(string Kind, int Waiting);

/// <summary>Respuesta de <c>GET /tasks/work/office/waiting</c>: tareas, pedidos y, con su permiso, form requests.</summary>
public sealed record OfficeWaitingResponse(IReadOnlyList<OfficeWaitingCount> Counts);

/// <param name="CanViewOffice">El actor tiene <c>users.work.view</c>.</param>
/// <param name="CanViewAll">El actor tiene <c>customers.view_all</c>.</param>
public sealed record GetOfficeWaitingQuery(Guid TenantId, Guid ActorUserId, bool CanViewOffice, bool CanViewAll);

/// <summary>
/// "Waiting on clients" de la oficina: sin filtro de responsable, pero con el mismo ABAC de quien mira que el resto
/// del trabajo. Exige <c>users.work.view</c>, como ver el trabajo de otra persona.
/// </summary>
public static class GetOfficeWaitingHandler
{
    public static async Task<Result<IReadOnlyList<OfficeWaitingCount>>> Handle(
        GetOfficeWaitingQuery query,
        ITaskWorkReader reader,
        IOptions<TasksVisibilityOptions> visibility,
        CancellationToken ct
    )
    {
        if (!query.CanViewOffice)
            return Result.Failure<IReadOnlyList<OfficeWaitingCount>>(WorkErrors.Forbidden);

        var visibleTo = WorkAccess.VisibleTo(query.ActorUserId, query.CanViewAll, visibility.Value.Enabled);
        return Result.Success(await reader.CountOfficeWaitingAsync(query.TenantId, visibleTo, ct));
    }
}
