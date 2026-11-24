using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Tasks.Domain.Series.Events;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;

namespace TaxVision.Tasks.Domain.Series;

/// <summary>
/// La regla, no las tareas. Vive más que cualquier ocurrencia y mantiene una sola instancia abierta a
/// la vez: nadie ve cuarenta «941 trimestral» en su lista.
/// </summary>
public sealed class TaskSeries : AggregateRoot
{
    private TaskSeries() { }

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
    /// La fecha que el motor planificó para la última ocurrencia. La siguiente se siembra desde aquí, no desde el
    /// vencimiento que tenga la tarea: mover una tarea (una prórroga) no corre las siguientes.
    /// </summary>
    public DateTime? LastPlannedDueUtc { get; private set; }

    /// <summary>
    /// Primera fecha de un calendario nuevo puesto al editar la serie. La próxima ocurrencia es esta; después se
    /// encadena desde ella. Se consume al materializar.
    /// </summary>
    public DateTime? PendingFirstDueUtc { get; private set; }

    /// <summary>
    /// Las que quedaron atrás por un atraso largo en <see cref="RecurrenceMode.FixedSchedule"/>. Se
    /// cuentan en vez de materializarlas: ocho instancias vencidas de golpe son el desorden que este
    /// diseño evita, pero callarlas dejaría al usuario sin saber que se saltearon.
    /// </summary>
    public int SkippedOccurrences { get; private set; }

    public DateTime? EndsAtUtc { get; private set; }
    public int? MaxOccurrences { get; private set; }

    /// <summary>
    /// En una serie anual con año fiscal, el año del vencimiento al que corresponde el año fiscal del molde (D-12). Nulo:
    /// el año fiscal no avanza (varias fechas al año, o sin año fiscal).
    /// </summary>
    public int? TaxYearDueYear { get; private set; }

    /// <summary>Plantilla de la que salió la serie: cada ocurrencia lleva su vínculo y sus archivos de referencia.</summary>
    public Guid? TemplateId { get; private set; }

    /// <summary>Intentos del barrido sin éxito seguidos; vuelve a 0 al crear la ocurrencia.</summary>
    public int MaterializationAttempts { get; private set; }

    /// <summary>Antes de esta hora el barrido no la vuelve a intentar. Nula = cuando toque.</summary>
    public DateTime? NextMaterializationAtUtc { get; private set; }

    private static readonly TimeSpan FirstRetry = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan LongestRetry = TimeSpan.FromDays(1);

    [UserReference(UserReferenceKind.Provenance, Note = "Quien la gestiona es EffectiveOwnerUserId.")]
    public Guid CreatedByUserId { get; private set; }

    /// <summary>
    /// Dueño operativo cuando no es el creador (p. ej. el creador fue retirado). Nulo = el creador. No
    /// reemplaza a <see cref="CreatedByUserId"/>, que es procedencia y no cambia.
    /// </summary>
    [UserReference(UserReferenceKind.ActiveWork, HandledBy = "TasksUserOffboardedConsumer")]
    public Guid? OwnerUserId { get; private set; }

    /// <summary>Quién gestiona la serie y figura como creador de las ocurrencias nuevas.</summary>
    public Guid EffectiveOwnerUserId => OwnerUserId ?? CreatedByUserId;
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

        if (!Enum.IsDefined(mode) || !Enum.IsDefined(blueprint.Priority))
            return Result.Failure<TaskSeries>(TaskErrors.Series.OptionInvalid);

        var first = FirstDueFrom(rule, mode, anchorUtc);
        if (first.IsFailure)
            return Result.Failure<TaskSeries>(first.Error);
        anchorUtc = first.Value;

        if (endsAtUtc is { } ends && ends < anchorUtc)
            return Result.Failure<TaskSeries>(TaskErrors.Series.EndsBeforeAnchor);

        var series = new TaskSeries
        {
            TaxYearDueYear = TaxYearBase(rule, blueprint, anchorUtc),
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
        series.SetTenant(tenantId);
        return Result.Success(series);
    }

    /// <summary>
    /// El cliente y el año fiscal de la ocurrencia que vence en <paramref name="dueUtc"/>: en una serie anual el año
    /// avanza con el vencimiento (el 1040 de abril de 2028 es del año fiscal 2027).
    /// </summary>
    public TaskReference ReferenceFor(DateTime dueUtc)
    {
        var reference = Blueprint.Reference;
        if (TaxYearDueYear is not { } baseYear || reference.TaxYear is not { } taxYear)
            return reference;
        var dueYear = CivilDate.Of(dueUtc, ResolvedTimeZone.Resolve(Rule.TimeZoneId)).Year;
        var moved = TaskReference.Create(reference.CustomerId, taxYear + dueYear - baseYear);
        return moved.IsSuccess ? moved.Value : reference;
    }

    private static int? TaxYearBase(RecurrenceRule rule, TaskItemBlueprint blueprint, DateTime firstDueUtc) =>
        rule.IsAnnual && blueprint.Reference.TaxYear is not null
            ? CivilDate.Of(firstDueUtc, ResolvedTimeZone.Resolve(rule.TimeZoneId)).Year
            : null;

    /// <summary>
    /// En calendario fijo con fecha civil, la primera tarea es la primera fecha de la regla desde el día elegido (C2): el
    /// ancla es "empieza el", no una fecha que la regla quizá no tenga. Con hora, sin días fijos en la regla o "después de
    /// completar" no cambia.
    /// </summary>
    private static Result<DateTime> FirstDueFrom(RecurrenceRule rule, RecurrenceMode mode, DateTime anchorUtc)
    {
        var zone = ResolvedTimeZone.Resolve(rule.TimeZoneId);
        if (mode != RecurrenceMode.FixedSchedule || !rule.FixesItsDays || anchorUtc != EndOfDayOf(anchorUtc, zone))
            return Result.Success(anchorUtc);
        return RecurrencePreview.FirstDueUtc(rule, mode, CivilDate.Of(anchorUtc, zone));
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
        // 1040-ES del 15 de abril espera esa fecha, no el 15 de julio. Con plazo legal, aunque ya haya pasado (D-10).
        if (
            GeneratedCount == 0
            && LastPlannedDueUtc is null
            && lastDueUtc is null
            && (AnchorUtc > nowUtc || KeepsEveryOccurrence)
        )
            return Result.Success(new SeriesOccurrence(AnchorUtc, 1, 0));

        // El calendario cambió al editar: la próxima es su primera fecha, aunque el modo sea "después de completar".
        if (PendingFirstDueUtc is { } pending)
            return PlanFrom(pending, nowUtc);

        // "N meses después de completar" suma meses recortando al último día (D-15): la regla saltaría los cortos.
        if (
            Mode == RecurrenceMode.AfterCompletion
            && completedAtUtc is { } completed
            && Rule.PlainMonthlyInterval() is { } months
        )
            return WithinEnd(AddMonthsAfterCompletion(completed, months));

        // Los dos modos comparten el RRULE; lo único que cambia es desde dónde se expande.
        var seed = Mode switch
        {
            // Una serie de fecha civil cuenta desde el día en que se cerró, no desde la hora (P4).
            RecurrenceMode.AfterCompletion when completedAtUtc is { } done && IsCivil(out var zone) => EndOfDayOf(
                done,
                zone
            ),
            RecurrenceMode.AfterCompletion => completedAtUtc ?? LastPlannedDueUtc ?? lastDueUtc ?? AnchorUtc,
            _ => LastPlannedDueUtc ?? lastDueUtc ?? AnchorUtc,
        };

        return PlanAfter(seed, nowUtc, 0);
    }

    /// <summary>La primera fecha del calendario nuevo; si ya pasó, la siguiente que no haya pasado (salvo plazo legal).</summary>
    private Result<SeriesOccurrence> PlanFrom(DateTime pendingUtc, DateTime nowUtc)
    {
        if (EndsAtUtc is { } ends && pendingUtc > ends)
            return Result.Failure<SeriesOccurrence>(TaskErrors.Series.NoFurtherOccurrence);
        return pendingUtc > nowUtc || KeepsEveryOccurrence
            ? Result.Success(new SeriesOccurrence(pendingUtc, GeneratedCount + 1, 0))
            : PlanAfter(pendingUtc, nowUtc, 1);
    }

    private Result<SeriesOccurrence> PlanAfter(DateTime seed, DateTime nowUtc, int alreadySkipped)
    {
        var skipped = alreadySkipped;
        var candidate = seed;
        while (true)
        {
            var next = Rule.NextAfter(candidate);
            if (next.IsFailure)
                return Result.Failure<SeriesOccurrence>(next.Error);

            candidate = next.Value;

            if (EndsAtUtc is { } ends && candidate > ends)
                return Result.Failure<SeriesOccurrence>(TaskErrors.Series.NoFurtherOccurrence);

            // Una obligación legal no desaparece porque la anterior se cerró tarde: se crea aunque ya venció (D-10).
            if (candidate > nowUtc || KeepsEveryOccurrence)
                return Result.Success(new SeriesOccurrence(candidate, GeneratedCount + 1, skipped));

            skipped++;
        }
    }

    /// <summary>Calendario fijo con plazo legal: no se salta ninguna ocurrencia.</summary>
    private bool KeepsEveryOccurrence => Mode == RecurrenceMode.FixedSchedule && Blueprint.IsStatutory;

    private Result<SeriesOccurrence> WithinEnd(DateTime dueUtc) =>
        EndsAtUtc is { } ends && dueUtc > ends
            ? Result.Failure<SeriesOccurrence>(TaskErrors.Series.NoFurtherOccurrence)
            : Result.Success(new SeriesOccurrence(dueUtc, GeneratedCount + 1, 0));

    private DateTime AddMonthsAfterCompletion(DateTime completedUtc, int months) =>
        IsCivil(out var zone) ? AddMonthsToCivilDay(completedUtc, months, zone) : completedUtc.AddMonths(months);

    /// <summary>
    /// El mismo día N meses después, o el último del mes si no existe (31-ene + 1 = 28-feb), al final del día de la
    /// oficina. Lo usa también la vista previa.
    /// </summary>
    internal static DateTime AddMonthsToCivilDay(DateTime instantUtc, int months, ResolvedTimeZone zone) =>
        LocalDay.EndUtc(CivilDate.Of(instantUtc, zone).AddMonths(months), zone);

    public Result RegisterMaterialized(Guid taskId, SeriesOccurrence occurrence)
    {
        if (Status != SeriesStatus.Active)
            return Result.Failure(TaskErrors.Series.NotActive);

        if (OpenInstanceId is not null)
            return Result.Failure(TaskErrors.Series.InstanceStillOpen);

        OpenInstanceId = taskId;
        GeneratedCount = occurrence.Number;
        LastPlannedDueUtc = occurrence.DueAtUtc;
        PendingFirstDueUtc = null;
        MaterializationAttempts = 0;
        NextMaterializationAtUtc = null;
        SkippedOccurrences += occurrence.Skipped;

        if (MaxOccurrences is { } max && GeneratedCount >= max)
        {
            Status = SeriesStatus.Ended;
            AnnounceEnded();
        }

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

    /// <summary>
    /// El barrido la toma antes de intentarlo, en su propia transacción: si el intento falla, no vuelve hasta que pase
    /// la espera (10 minutos que se duplican, como mucho un día) y no ocupa el lugar de las demás.
    /// </summary>
    public void ClaimForMaterialization(DateTime nowUtc)
    {
        MaterializationAttempts++;
        var minutes = FirstRetry.TotalMinutes * Math.Pow(2, Math.Min(MaterializationAttempts - 1, 20));
        NextMaterializationAtUtc = nowUtc + TimeSpan.FromMinutes(Math.Min(minutes, LongestRetry.TotalMinutes));
    }

    public void OriginateFrom(Guid templateId) => TemplateId = templateId;

    /// <summary>Reabrir una ocurrencia que la serie ya reemplazó dejaría dos abiertas.</summary>
    public Result CanReopenOccurrence(Guid taskId, int occurrenceNumber) =>
        OpenInstanceId != taskId && GeneratedCount > occurrenceNumber
            ? Result.Failure(TaskErrors.Series.OccurrenceSuperseded)
            : Result.Success();

    /// <summary>
    /// La última ocurrencia se reabrió: vuelve a ser la abierta de la serie, para que la serie no siembre otra mientras
    /// tanto (p. ej. al reanudarla). Una serie terminada no revive.
    /// </summary>
    public Result RegisterInstanceReopened(Guid taskId, int occurrenceNumber)
    {
        var allowed = CanReopenOccurrence(taskId, occurrenceNumber);
        if (allowed.IsFailure)
            return allowed;
        if (Status != SeriesStatus.Ended && OpenInstanceId is null)
            OpenInstanceId = taskId;
        return Result.Success();
    }

    /// <summary>
    /// Reasigna el asignado por defecto del blueprint al RETIRAR (offboard) al actual — solo redirige
    /// las ocurrencias FUTURAS (las ya materializadas son TaskItem aparte, se reasignan una a una). No
    /// sobre series terminadas. Idempotente. No toca <see cref="CreatedByUserId"/> (procedencia).
    /// </summary>
    public Result ReassignBlueprintAssignee(Guid newAssigneeUserId)
    {
        if (newAssigneeUserId == Guid.Empty)
            return Result.Failure(TaskErrors.AssigneeRequired);

        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        if (Blueprint.AssigneeUserId == newAssigneeUserId)
            return Result.Success();

        Blueprint = Blueprint with { AssigneeUserId = newAssigneeUserId, Reference = Blueprint.Reference with { } };
        return Result.Success();
    }

    /// <summary>
    /// Pasa la gestión de la serie (y la autoría de las ocurrencias nuevas) a otra persona, p. ej. al retirar
    /// a su dueño. No sobre series terminadas. Idempotente. No toca <see cref="CreatedByUserId"/>.
    /// </summary>
    public Result TransferOwnership(Guid newOwnerUserId)
    {
        if (newOwnerUserId == Guid.Empty)
            return Result.Failure(TaskErrors.OwnerRequired);

        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        OwnerUserId = newOwnerUserId == CreatedByUserId ? null : newOwnerUserId;
        return Result.Success();
    }

    /// <summary>
    /// Cambia el molde y el fin. Vale para las ocurrencias futuras; las ya creadas son tareas aparte. El límite no puede
    /// bajar de las ya creadas y el fin no puede quedar antes de la ocurrencia en curso.
    /// </summary>
    public Result Revise(TaskItemBlueprint blueprint, DateTime? endsAtUtc, int? maxOccurrences)
    {
        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        if (blueprint.AssigneeUserId == Guid.Empty)
            return Result.Failure(TaskErrors.AssigneeRequired);

        if (!Enum.IsDefined(blueprint.Priority))
            return Result.Failure(TaskErrors.Series.OptionInvalid);

        if (maxOccurrences is { } max && (max <= 0 || max < GeneratedCount))
            return Result.Failure(TaskErrors.Series.MaxOccurrencesInvalid);

        if (endsAtUtc is { } ends && ends < (LastPlannedDueUtc ?? AnchorUtc))
            return Result.Failure(TaskErrors.Series.EndsBeforeNext);

        // Referencia propia: EF pierde la anidada si el molde nuevo comparte la instancia del anterior.
        Blueprint = blueprint with
        {
            Reference = blueprint.Reference with { },
        };
        EndsAtUtc = endsAtUtc;
        MaxOccurrences = maxOccurrences;
        return Result.Success();
    }

    /// <summary>
    /// Pone un calendario nuevo. La ocurrencia abierta se queda como está; la siguiente vence en
    /// <paramref name="firstDueUtc"/>, que tiene que ser posterior a la última planificada.
    /// </summary>
    public Result Reschedule(RecurrenceRule rule, RecurrenceMode mode, DateTime firstDueUtc)
    {
        if (Status == SeriesStatus.Ended)
            return Result.Failure(TaskErrors.Series.AlreadyEnded);

        if (!Enum.IsDefined(mode))
            return Result.Failure(TaskErrors.Series.OptionInvalid);

        if (firstDueUtc.Kind != DateTimeKind.Utc)
            return Result.Failure(TaskErrors.Series.AnchorNotUtc);

        if (GeneratedCount > 0 && firstDueUtc <= (LastPlannedDueUtc ?? AnchorUtc))
            return Result.Failure(TaskErrors.Series.StartsBeforeCurrent);

        TaxYearDueYear = rule.IsAnnual ? TaxYearDueYear ?? TaxYearBase(rule, Blueprint, firstDueUtc) : null;
        Rule = rule;
        Mode = mode;
        if (GeneratedCount == 0)
            AnchorUtc = firstDueUtc;
        else
            PendingFirstDueUtc = firstDueUtc;
        return Result.Success();
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

        if (Status != SeriesStatus.Paused)
            return Result.Failure(TaskErrors.Series.NotPaused);

        Status = SeriesStatus.Active;
        AnchorUtc = IsCivil(out var zone) ? EndOfDayOf(nowUtc, zone) : nowUtc;
        // Lo que tocaba durante la pausa no vuelve: la siguiente se siembra desde hoy.
        LastPlannedDueUtc = AnchorUtc;
        return Result.Success();
    }

    /// <summary>
    /// P9: la oficina cambió de zona. Una serie activa con la regla en la zona anterior pasa a la nueva; si era de fecha
    /// civil, su ancla queda al fin del mismo día en la nueva.
    /// </summary>
    public bool RezoneCivilDue(string previousTimeZoneId, ResolvedTimeZone newZone)
    {
        // También las pausadas: al reanudar tienen que vencer en la zona nueva.
        if (Status == SeriesStatus.Ended || Rule.TimeZoneId != previousTimeZoneId)
            return false;
        var wasCivil = IsCivil(out var previousZone);
        Rule = Rule.InTimeZone(newZone.Id);
        if (wasCivil)
        {
            AnchorUtc = LocalDay.EndUtc(CivilDate.Of(AnchorUtc, previousZone), newZone);
            if (LastPlannedDueUtc is { } planned)
                LastPlannedDueUtc = LocalDay.EndUtc(CivilDate.Of(planned, previousZone), newZone);
            if (PendingFirstDueUtc is { } pending)
                PendingFirstDueUtc = LocalDay.EndUtc(CivilDate.Of(pending, previousZone), newZone);
        }
        return true;
    }

    /// <summary>Serie de fecha civil: su ancla es el final de un día en la zona de la regla (P4).</summary>
    private bool IsCivil(out ResolvedTimeZone zone)
    {
        zone = ResolvedTimeZone.Resolve(Rule.TimeZoneId);
        return AnchorUtc == EndOfDayOf(AnchorUtc, zone);
    }

    private static DateTime EndOfDayOf(DateTime instantUtc, ResolvedTimeZone zone) =>
        LocalDay.EndUtc(CivilDate.Of(instantUtc, zone), zone);

    /// <summary>La termina una persona: no se avisa, ya lo sabe.</summary>
    public void End()
    {
        Status = SeriesStatus.Ended;
        OpenInstanceId = null;
    }

    /// <summary>La regla se agotó o pasó la fecha de fin: termina y se avisa a quien la gestiona (D-28).</summary>
    public void EndOnItsOwn()
    {
        if (Status == SeriesStatus.Ended)
            return;
        End();
        AnnounceEnded();
    }

    private void AnnounceEnded() =>
        AddDomainEvent(new TaskSeriesEndedDomainEvent(Id, TenantId, EffectiveOwnerUserId, Blueprint.Title.Value));
}

/// <summary>Lo que hay que crear: cuándo vence, qué número de ocurrencia es y cuántas se saltearon.</summary>
public sealed record SeriesOccurrence(DateTime DueAtUtc, int Number, int Skipped);
