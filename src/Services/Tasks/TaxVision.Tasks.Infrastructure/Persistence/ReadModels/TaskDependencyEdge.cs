namespace TaxVision.Tasks.Infrastructure.Persistence.ReadModels;

// Keyless: sólo existe para materializar las filas del CTE recursivo. No es una entidad.
public sealed class TaskDependencyEdge
{
    public Guid TaskId { get; init; }
    public Guid DependsOnTaskId { get; init; }
}
