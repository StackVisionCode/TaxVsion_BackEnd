using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Work;

/// <summary>
/// Form requests abiertas a cargo de una persona (responsable o, sin él, quien la pidió), con la visibilidad de la
/// bandeja para quien mira (<paramref name="canViewAll"/> = <c>customers.view_all</c>). Todo en SQL.
/// </summary>
public interface IFormRequestWorkReader
{
    Task<WorkCount> CountAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today,
        CancellationToken ct
    );

    Task<IReadOnlyList<WorkItem>> TopAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        int take,
        CancellationToken ct
    );

    /// <summary>Pendientes o devueltas de toda la oficina (cualquier responsable), con la bandeja de quien mira.</summary>
    Task<int> CountOfficeWaitingAsync(Guid tenantId, Guid actorUserId, bool canViewAll, CancellationToken ct);

    /// <summary>Abiertas y vencidas por persona; solo filas con algo.</summary>
    Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today,
        CancellationToken ct
    );

    /// <summary>Una página con el mismo universo que el conteo y el orden del top.</summary>
    Task<PagedResult<WorkItem>> ItemsAsync(
        Guid tenantId,
        Guid userId,
        Guid actorUserId,
        bool canViewAll,
        int page,
        int size,
        CancellationToken ct
    );

    /// <summary>Títulos sin recortar de lo que existe en el tenant y está en la bandeja de quien mira.</summary>
    Task<IReadOnlyList<WorkTitle>> TitlesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> requestIds,
        Guid actorUserId,
        bool canViewAll,
        CancellationToken ct
    );
}

public sealed record FormRequestWork(WorkCount Count, IReadOnlyList<WorkItem> Top)
{
    public static string LinkFor(Guid requestId) => $"/client-forms?formRequestId={requestId}";
}
