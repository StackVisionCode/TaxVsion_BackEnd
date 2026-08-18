namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// Un tramo de trabajo imputado a una tarea. Entidad hija: sólo la abre y la cierra
/// <see cref="TaskItem.StartTimer"/> / <see cref="TaskItem.StopTimer"/>.
/// </summary>
/// <remarks>
/// El <see cref="Id"/> lo genera el dominio, así que su config EF necesita
/// <c>ValueGeneratedNever()</c> o EF intenta un UPDATE en vez de un INSERT.
/// </remarks>
public sealed class TaskTimer
{
    public Guid Id { get; }
    public Guid TaskId { get; }
    public Guid UserId { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime? StoppedAtUtc { get; private set; }
    public bool IsBillable { get; }

    public bool IsRunning => StoppedAtUtc is null;

    /// <summary>Cero mientras corre: se imputa al cerrar, no mientras el reloj avanza.</summary>
    public decimal Hours => StoppedAtUtc is { } stopped ? ToHours(stopped - StartedAtUtc) : 0m;

    private TaskTimer(Guid id, Guid taskId, Guid userId, bool isBillable, DateTime startedAtUtc)
    {
        Id = id;
        TaskId = taskId;
        UserId = userId;
        IsBillable = isBillable;
        StartedAtUtc = startedAtUtc;
    }

    private TaskTimer() { }

    internal static TaskTimer Start(Guid taskId, Guid userId, bool isBillable, DateTime nowUtc) =>
        new(Guid.NewGuid(), taskId, userId, isBillable, nowUtc);

    internal void Stop(DateTime nowUtc) => StoppedAtUtc = nowUtc < StartedAtUtc ? StartedAtUtc : nowUtc;

    /// <summary>Dos decimales: es lo que se factura, y un tercero sólo agrega ruido al total.</summary>
    private static decimal ToHours(TimeSpan elapsed) => Math.Round((decimal)elapsed.TotalHours, 2);
}
