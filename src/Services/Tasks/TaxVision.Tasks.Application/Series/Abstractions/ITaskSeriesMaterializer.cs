using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Application.Series.Abstractions;

/// <summary>
/// Cruza dos agregados —la serie y la tarea— así que no vive en ninguno de los dos. No persiste:
/// muta lo rastreado y guarda el handler que la llamó, igual que la jerarquía y el desbloqueo.
/// </summary>
public interface ITaskSeriesMaterializer
{
    /// <summary>
    /// Crea la próxima ocurrencia y deja la serie apuntando a ella. Devuelve fallo cuando la regla se
    /// agotó, que es un final legítimo y no un error a propagar al usuario.
    /// </summary>
    Task<Result<TaskItem>> MaterializeNextAsync(
        TaskSeries series,
        DateTime? lastDueUtc,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    );

    /// <summary>
    /// La primera ocurrencia, en el alta de la serie: a diferencia de <see cref="MaterializeNextAsync"/>, avisa la
    /// asignación (D-TSK-1).
    /// </summary>
    Task<Result<TaskItem>> MaterializeFirstAsync(TaskSeries series, CancellationToken ct = default);

    /// <summary>
    /// La instancia abierta se cerró: libera la serie y siembra la siguiente en el mismo paso.
    /// Devuelve la ocurrencia nueva —o <c>null</c> si la tarea no era de una serie o la regla se
    /// agotó— para que quien la llamó le pida su recordatorio, igual que a una tarea creada a mano.
    /// </summary>
    Task<TaskItem?> ApplyInstanceClosedAsync(TaskItem task, DateTime? completedAtUtc, CancellationToken ct = default);

    /// <summary>Si la tarea es una ocurrencia que la serie ya reemplazó, no se puede reabrir.</summary>
    Task<Result> CanReopenAsync(TaskItem task, CancellationToken ct = default);

    /// <summary>Tras reabrir: la última ocurrencia vuelve a ser la abierta de su serie.</summary>
    Task RegisterReopenedAsync(TaskItem task, CancellationToken ct = default);
}
