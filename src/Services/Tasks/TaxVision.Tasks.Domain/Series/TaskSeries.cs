using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Series;

/// <summary>
/// La regla, no las tareas. Vive más que cualquier ocurrencia y mantiene una sola instancia abierta a
/// la vez: nadie ve cuarenta «941 trimestral» en su lista.
/// </summary>
public sealed class TaskSeries : BaseEntity, ITenantOwned
{
    private TaskSeries() { }

    public Guid TenantId { get; private set; }

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public RecurrenceRule Rule { get; private set; } = default!;
    public RecurrenceMode Mode { get; private set; }
    public SeriesStatus Status { get; private set; }

    public TaskItemBlueprint Blueprint { get; private set; } = default!;

    /// <summary>La semilla original. No se mueve al materializar.</summary>
    public DateTime AnchorUtc { get; private set; }

    /// <summary>La única abierta. Nula entre que se cierra una y se materializa la siguiente.</summary>
    public Guid? OpenInstanceId { get; private set; }

    public int GeneratedCount { get; private set; }

    /// <summary>
    /// Las que quedaron atrás por un atraso largo en <see cref="RecurrenceMode.FixedSchedule"/>. Se
    /// cuentan en vez de materializarlas: ocho instancias vencidas de golpe son el desorden que este
    /// diseño evita, pero callarlas dejaría al usuario sin saber que se saltearon.
    /// </summary>
    public int SkippedOccurrences { get; private set; }

    public DateTime? EndsAtUtc { get; private set; }
    public int? MaxOccurrences { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public byte[] RowVersion { get; private set; } = [];

    public static Result<TaskSeries> Create(
        Guid tenantId,
        Guid createdByUserId,
        RecurrenceRule rule,
        RecurrenceMode mode,
        TaskItemBlueprint blueprint,
        DateTime anchorUtc,
        DateTime? endsAtUtc,
        int? maxOccurrences,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || createdByUserId == Guid.Empty)
            return Result.Failure<TaskSeries>(TaskErrors.OwnerRequired);

        if (blueprint.AssigneeUserId == Guid.Empty)
            return Result.Failure<TaskSeries>(TaskErrors.AssigneeRequired);

        if (anchorUtc.Kind != DateTimeKind.Utc)
            return Result.Failure<TaskSeries>(TaskErrors.Series.AnchorNotUtc);

        if (maxOccurrences is <= 0)
            return Result.Failure<TaskSeries>(TaskErrors.Series.MaxOccurrencesInvalid);

        if (endsAtUtc is { } ends && ends < anchorUtc)
            return Result.Failure<TaskSeries>(TaskErrors.Series.EndsBeforeAnchor);

        var series = new TaskSeries
        {
            TenantId = tenantId,
            CreatedByUserId = createdByUserId,
            Rule = rule,
            Mode = mode,
            Blueprint = blueprint,
            Status = SeriesStatus.Active,
            AnchorUtc = anchorUtc,
            EndsAtUtc = endsAtUtc,
            MaxOccurrences = maxOccurrences,
            CreatedAtUtc = nowUtc,
        };
        return Result.Success(series);
    }

    /// <summary>
    /// Calcula la próxima ocurrencia sin tocar nada. Se separa de
    /// <see cref="RegisterMaterialized"/> porque entre las dos hay que crear la tarea, y una tarea que
    /// no se pudo crear no debe dejar a la serie con los contadores movidos.
    /// </summary>
    public Result<SeriesOccurrence> PlanNextOccurrence(DateTime? lastDueUtc, DateTime? completedAtUtc, DateTime nowUtc)
    {
        if (Status != SeriesStatus.Active)
            return Result.Failure<SeriesOccurrence>(TaskErrors.Series.NotActive);

        if (OpenInstanceId is not null)
            return Result.Failure<SeriesOccurrence>(TaskErrors.Series.InstanceStillOpen);

        if (MaxOccurrences is { } max && GeneratedCount >= max)
            return Result.Failure<SeriesOccurrence>(TaskErrors.Series.NoFurtherOccurrence);

        // La primera ocurrencia ES el ancla, no la siguiente que salga de la regla: quien pone el
        // 1040-ES del 15 de abril espera esa fecha, no el 15 de julio.
        if (GeneratedCount == 0 && lastDueUtc is null && AnchorUtc > nowUtc)
            return Result.Success(new SeriesOccurrence(AnchorUtc, 1, 0));

        // Los dos modos comparten el RRULE; lo único que cambia es desde dónde se expande.
        var seed = Mode switch
        {
            RecurrenceMode.AfterCompletion => completedAtUtc ?? lastDueUtc ?? AnchorUtc,
            _ => lastDueUtc ?? AnchorUtc,
        };

        var skipped = 0;
        var candidate = seed;
        while (true)
        {
            var next = Rule.NextAfter(candidate);
            if (next.IsFailure)
                return Result.Failure<SeriesOccurrence>(next.Error);

            candidate = next.Value;

            if (EndsAtUtc is { } ends && candidate > ends)
                return Result.Failure<SeriesOccurrence>(TaskErrors.Series.NoFurtherOccurrence);

            if (candidate > nowUtc)
                return Result.Success(new SeriesOccurrence(candidate, GeneratedCount + 1, skipped));

            skipped++;
        }
    }

    public Result RegisterMaterialized(Guid taskId, SeriesOccurrence occurrence)
    {
        if (Status != SeriesStatus.Active)
            return Result.Failure(TaskErrors.Series.NotActive);

        if (OpenInstanceId is not null)
            return Result.Failure(TaskErrors.Series.InstanceStillOpen);

        OpenInstanceId = taskId;
        GeneratedCount = occurrence.Number;
        SkippedOccurrences += occurrence.Skipped;

        if (MaxOccurrences is { } max && GeneratedCount >= max)
            Status = SeriesStatus.Ended;

        return Result.Success();
    }

    /// <summary>
    /// Sólo la instancia abierta libera la serie. Cerrar una vieja —un reintento, o completar una
    /// ocurrencia ya reemplazada— no debe dejar hueco para materializar dos.
    /// </summary>
    public bool RegisterInstanceClosed(Guid taskId)
    {
        if (OpenInstanceId != taskId)
            return false;

        OpenInstanceId = null;
        return true;
    }

    public Result Pause()
    {
        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        Status = SeriesStatus.Paused;
        return Result.Success();
    }

    /// <summary>
    /// Reanudar siembra desde ahora, no desde donde quedó: si estuvo pausada un año, nadie quiere que
    /// vuelva con la ocurrencia del año pasado.
    /// </summary>
    public Result Resume(DateTime nowUtc)
    {
        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        Status = SeriesStatus.Active;
        AnchorUtc = nowUtc;
        return Result.Success();
    }

    public void End()
    {
        Status = SeriesStatus.Ended;
        OpenInstanceId = null;
    }
}

/// <summary>Lo que hay que crear: cuándo vence, qué número de ocurrencia es y cuántas se saltearon.</summary>
public sealed record SeriesOccurrence(DateTime DueAtUtc, int Number, int Skipped);
