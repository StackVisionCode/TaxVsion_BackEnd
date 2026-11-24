namespace TaxVision.Tasks.Application.Tasks.Abstractions;

/// <summary>Una tarea borrada (la raíz de lo que se fue a la papelera).</summary>
public sealed record TaskTrashItem(
    Guid TaskId,
    string Title,
    Guid? CustomerId,
    DateTime DeletedAtUtc,
    Guid? DeletedByUserId,
    int SubtaskCount,
    DateTime PurgeOnUtc
);

public sealed record TaskTrashPage(IReadOnlyList<TaskTrashItem> Items, int TotalCount);

public interface ITaskTrashReader
{
    /// <summary>Lo borrado de la oficina que el usuario puede ver, lo más reciente primero.</summary>
    Task<TaskTrashPage> ListAsync(Guid tenantId, Guid? visibleTo, int page, int size, CancellationToken ct = default);

    /// <summary>Raíces que llevan más de los días de retención en la papelera, de todas las oficinas (la purga).</summary>
    Task<IReadOnlyList<(Guid TenantId, Guid RootId)>> ListExpiredRootsAsync(
        DateTime deletedBeforeUtc,
        int take,
        CancellationToken ct = default
    );
}
