using BuildingBlocks.Domain;

namespace TaxVision.Tasks.Domain.Tasks;

/// <summary>
/// Un tramo de trabajo imputado a una tarea. Entidad hija: lo abre y lo cierra el temporizador
/// (<see cref="TaskItem.StartTimer"/> / <see cref="TaskItem.StopTimer"/>) o nace cerrado al anotar tiempo a mano
/// (<see cref="TaskItem.LogTime"/>). Cerrado, su autor lo corrige o lo borra.
/// </summary>
/// <remarks>
/// El <see cref="Id"/> lo genera el dominio, así que su config EF necesita
/// <c>ValueGeneratedNever()</c> o EF intenta un UPDATE en vez de un INSERT.
/// </remarks>
public sealed class TaskTimer
{
    public Guid Id { get; }
    public Guid TaskId { get; }

    [UserReference(
        UserReferenceKind.Private,
        HandledBy = "TaskTimersUserOffboardedConsumer",
        Note = "El sistema cierra sus timers en marcha."
    )]
    public Guid UserId { get; }
    public DateTime StartedAtUtc { get; }
    public DateTime? StoppedAtUtc { get; private set; }
    public bool IsBillable { get; private set; }

    /// <summary>Anotado a mano: el tramo se deriva del día y la duración.</summary>
    public bool IsManual { get; private set; }
    public string? Note { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    [UserReference(UserReferenceKind.Provenance)]
    public Guid? UpdatedByUserId { get; private set; }

    public const int NoteMaxLength = 200;

    /// <summary>Un registro no pasa de un día de trabajo.</summary>
    public const int MaxMinutes = 24 * 60;

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

    internal static TaskTimer Logged(
        Guid taskId,
        Guid userId,
        DateTime startedAtUtc,
        int minutes,
        bool isBillable,
        string? note
    ) =>
        new(Guid.NewGuid(), taskId, userId, isBillable, startedAtUtc)
        {
            StoppedAtUtc = startedAtUtc.AddMinutes(minutes),
            IsManual = true,
            Note = note,
        };

    /// <summary>Corrección de un tramo cerrado: conserva el inicio y mueve el fin.</summary>
    internal void Correct(int minutes, bool isBillable, string? note, Guid byUserId, DateTime nowUtc)
    {
        StoppedAtUtc = StartedAtUtc.AddMinutes(minutes);
        IsBillable = isBillable;
        Note = note;
        UpdatedAtUtc = nowUtc;
        UpdatedByUserId = byUserId;
    }

    /// <summary>Dos decimales: es lo que se factura, y un tercero sólo agrega ruido al total.</summary>
    private static decimal ToHours(TimeSpan elapsed) => Math.Round((decimal)elapsed.TotalHours, 2);
}
