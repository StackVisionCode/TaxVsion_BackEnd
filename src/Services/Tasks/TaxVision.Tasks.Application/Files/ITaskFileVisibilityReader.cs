using BuildingBlocks.Contracts.Files;

namespace TaxVision.Tasks.Application.Files;

/// <summary>
/// Qué archivos de tareas ve quien mira: adjunto disponible en al menos una tarea que el detalle le mostraría.
/// <c>visibleTo</c> es el ABAC del listado (null = sin restricción). Un archivo, una fila: la referencia más reciente.
/// </summary>
public interface ITaskFileVisibilityReader
{
    Task<IReadOnlyList<VisibleFile>> VisibleTaskFilesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> fileIds,
        Guid? visibleTo,
        CancellationToken ct
    );
}
