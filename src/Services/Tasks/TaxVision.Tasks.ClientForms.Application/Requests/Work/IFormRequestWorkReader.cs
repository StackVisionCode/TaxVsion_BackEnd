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

    /// <summary>Abiertas y vencidas por persona; solo filas con algo.</summary>
    Task<IReadOnlyList<WorkBatchRow>> CountByUserAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> userIds,
        Guid actorUserId,
        bool canViewAll,
        DateOnly today,
        CancellationToken ct
    );
}

public sealed record FormRequestWork(WorkCount Count, IReadOnlyList<WorkItem> Top)
{
    public static string LinkFor(Guid requestId) => $"/client-forms?formRequestId={requestId}";
}
