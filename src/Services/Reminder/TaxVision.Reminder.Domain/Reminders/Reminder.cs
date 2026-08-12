using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Reminder.Domain.Reminders.Events;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Domain.Reminders;

/// <summary>
/// «Recordame X en T». Pertenece a <b>exactamente un usuario</b> (el que lo creó = el que lo recibe)
/// y apunta a cero o un objetivo por ID opaco.
///
/// <para>
/// <b>El aggregate no conoce Quartz.</b> Sólo junta hechos de dominio; traducirlos a
/// <c>ScheduleJob</c>/<c>RescheduleJob</c>/<c>DeleteJob</c> es de Infrastructure. Eso es lo que lo
/// mantiene testeable sin scheduler y sin base de datos.
/// </para>
///
/// <para>
/// <b>Ningún método recibe un <see cref="ReminderStatus"/>.</b> Cada transición tiene su método, su
/// validación de origen y su evento — el incidente que fundamenta esta regla en este monorepo es
/// <c>Customer.ChangeStatus(...)</c>, que hubo que partir retroactivamente en tres porque escondía
/// cinco reglas de negocio detrás de un <c>switch</c>.
/// </para>
/// </summary>
public sealed class Reminder : AggregateRoot, IHasOwner
{
    /// <summary>Invariante R4. Sin tope, un snooze infinito convierte el recordatorio en ruido eterno.</summary>
    public const int MaxSnoozeCount = 10;

    private Reminder() { } // EF Core

    /// <summary>Destinatario Y creador. v1: siempre la misma persona (los compartidos son non-goal).</summary>
    public Guid UserId { get; private set; }

    public ReminderSubject Subject { get; private set; } = default!;
    public ReminderTarget Target { get; private set; } = default!;
    public ReminderSchedule Schedule { get; private set; } = default!;
    public ReminderTimeZone TimeZone { get; private set; } = default!;
    public ReminderStatus Status { get; private set; }

    /// <summary>Clave de idempotencia (ADR-R-07). Índice ÚNICO junto con <c>TenantId</c>.</summary>
    public RequestKey RequestKey { get; private set; } = default!;

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? FiredAtUtc { get; private set; }

    /// <summary>Cuándo llegó a un estado terminal: <c>Dismissed</c>, <c>Cancelled</c> o <c>Missed</c>.</summary>
    public DateTime? ResolvedAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }
    public int SnoozeCount { get; private set; }

    /// <summary>Concurrencia optimista: Quartz puede disparar mientras el usuario pospone.</summary>
    public byte[] RowVersion { get; private set; } = [];

    /// <summary>
    /// RBAC Fase 4 — un recordatorio es estrictamente privado de su dueño. A diferencia de Notes,
    /// acá <b>no hay permiso de override</b>: ni <c>PlatformAdmin</c> lee recordatorios ajenos.
    /// </summary>
    Guid IHasOwner.CreatedByUserId => UserId;

    public static Result<Reminder> Create(
        Guid tenantId,
        Guid userId,
        ReminderSubject subject,
        ReminderTarget target,
        ReminderSchedule schedule,
        ReminderTimeZone timeZone,
        RequestKey requestKey,
        DateTime nowUtc
    )
    {
        // R1 — sin dueño no hay a quién avisar, y con Guid.Empty el filtro de tenant nunca casaría.
        if (tenantId == Guid.Empty || userId == Guid.Empty)
            return Result.Failure<Reminder>(ReminderErrors.OwnerRequired);

        var reminder = new Reminder
        {
            UserId = userId,
            Subject = subject,
            Target = target,
            Schedule = schedule,
            TimeZone = timeZone,
            RequestKey = requestKey,
            Status = ReminderStatus.Scheduled,
            CreatedAtUtc = nowUtc,
        };
        reminder.SetTenant(tenantId);

        reminder.AddDomainEvent(
            new ReminderScheduledDomainEvent(reminder.Id, tenantId, userId, target.Category, schedule.FireAtUtc)
        );
        return Result.Success(reminder);
    }

    /// <summary>
    /// Lo invoca el job de Quartz. <b>Idempotente</b>: si ya está <c>Fired</c> devuelve éxito sin
    /// re-emitir el evento, porque en un failover de cluster el mismo trigger puede ejecutarse dos
    /// veces y el usuario no debe recibir el aviso dos veces. Mismo criterio que
    /// <c>CodeReservation.Expire</c> en Growth.
    /// </summary>
    public Result MarkFired(DateTime nowUtc)
    {
        if (Status == ReminderStatus.Fired)
            return Result.Success();

        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(MarkFired)));

        Status = ReminderStatus.Fired;
        FiredAtUtc = nowUtc;
        AddDomainEvent(new ReminderFiredDomainEvent(Id, TenantId, UserId, Target.Category, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// Posponer. Recalcula el schedule como <b>absoluto</b> (<c>nowUtc + duration</c>): un snooze
    /// rompe el anclaje a propósito — el usuario pidió «en 10 minutos», no «10 minutos antes de la
    /// cita», y si el objetivo se moviera después no tendría sentido arrastrar este disparo.
    /// </summary>
    public Result Snooze(TimeSpan duration, DateTime nowUtc)
    {
        if (Status != ReminderStatus.Fired)
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(Snooze)));

        if (duration <= TimeSpan.Zero)
            return Result.Failure(ReminderErrors.SnoozeDurationInvalid);

        if (SnoozeCount >= MaxSnoozeCount)
            return Result.Failure(ReminderErrors.SnoozeLimitReached);

        var newSchedule = ReminderSchedule.Absolute(nowUtc.Add(duration), nowUtc);
        if (newSchedule.IsFailure)
            return Result.Failure(newSchedule.Error);

        var previousFireAtUtc = Schedule.FireAtUtc;
        Schedule = newSchedule.Value;
        Status = ReminderStatus.Snoozed;
        SnoozeCount++;

        AddDomainEvent(new ReminderSnoozedDomainEvent(Id, TenantId, SnoozeCount, Schedule.FireAtUtc));
        AddDomainEvent(new ReminderRescheduledDomainEvent(Id, TenantId, previousFireAtUtc, Schedule.FireAtUtc));
        return Result.Success();
    }

    /// <summary>El usuario lo vio y lo cerró. Terminal.</summary>
    public Result Dismiss(DateTime nowUtc)
    {
        if (Status is not (ReminderStatus.Fired or ReminderStatus.Snoozed))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(Dismiss)));

        Status = ReminderStatus.Dismissed;
        ResolvedAtUtc = nowUtc;
        AddDomainEvent(new ReminderDismissedDomainEvent(Id, TenantId, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// Cancelar. Razón obligatoria (<c>user_request</c>, <c>target_closed</c>) porque sin ella es
    /// imposible distinguir en soporte «lo cancelé yo» de «se completó la tarea».
    /// </summary>
    public Result Cancel(string reason, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(ReminderErrors.CancellationReasonRequired);

        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed or ReminderStatus.Fired))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(Cancel)));

        Status = ReminderStatus.Cancelled;
        CancellationReason = reason.Trim();
        ResolvedAtUtc = nowUtc;
        AddDomainEvent(new ReminderCancelledDomainEvent(Id, TenantId, CancellationReason, nowUtc));
        return Result.Success();
    }

    /// <summary>
    /// Reacción a <c>reminder.target_moved.v1</c>.
    ///
    /// <para>
    /// Sobre un recordatorio <b>absoluto</b> es un <b>no-op exitoso</b> (invariante R6): el usuario
    /// eligió «el jueves a las 9 pase lo que pase», y devolver error aquí haría fallar al consumer
    /// por un caso que es correcto por diseño.
    /// </para>
    ///
    /// <para>
    /// Si el objetivo se movió hacia atrás y el disparo recalculado <b>ya pasó</b>, no se reagenda:
    /// se transiciona a <c>Missed</c>. Avisar de algo cuya hora ya pasó es ruido.
    /// </para>
    /// </summary>
    public Result RescheduleToNewAnchor(DateTime newAnchorUtc, DateTime nowUtc)
    {
        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(RescheduleToNewAnchor)));

        if (!Schedule.IsAnchored)
            return Result.Success();

        var recalculated = Schedule.WithNewAnchor(newAnchorUtc, nowUtc);
        if (recalculated.IsFailure)
            return Result.Failure(recalculated.Error);

        var previousFireAtUtc = Schedule.FireAtUtc;
        Schedule = recalculated.Value;

        if (Schedule.FireAtUtc <= nowUtc)
            return TransitionToMissed(nowUtc);

        Status = ReminderStatus.Scheduled;
        AddDomainEvent(new ReminderRescheduledDomainEvent(Id, TenantId, previousFireAtUtc, Schedule.FireAtUtc));
        return Result.Success();
    }

    /// <summary>
    /// Lo que ejecuta el job del scheduler. Quartz <b>siempre</b> dispara al recuperarse de una
    /// caída, así que la pregunta real no es «¿toca?» sino «¿sigue vigente?»: si el retraso supera
    /// la ventana de gracia, el aviso se descarta como <c>Missed</c> en vez de llegar tarde.
    ///
    /// <para>
    /// La ventana entra por parámetro porque es configuración (<c>Reminder:MisfireGraceMinutes</c>),
    /// no una constante del dominio — pero la <b>decisión</b> es de negocio y vive acá, no en el
    /// adaptador de Quartz.
    /// </para>
    /// </summary>
    public Result FireOrMiss(DateTime nowUtc, TimeSpan misfireGrace) =>
        nowUtc - Schedule.FireAtUtc > misfireGrace ? MarkMissed(nowUtc) : MarkFired(nowUtc);

    /// <summary>
    /// Descarta el aviso por llegar tarde. Público además de por <see cref="FireOrMiss"/> porque
    /// <see cref="RescheduleToNewAnchor"/> también llega a este estado por otra vía.
    /// </summary>
    public Result MarkMissed(DateTime nowUtc)
    {
        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(MarkMissed)));

        return TransitionToMissed(nowUtc);
    }

    /// <summary>
    /// Edición explícita del usuario — distinta de <see cref="RescheduleToNewAnchor"/>, que es
    /// reacción a un evento del objetivo. Acá el usuario decide, así que su schedule manda incluso
    /// si rompe el anclaje anterior.
    /// </summary>
    public Result ChangeSchedule(ReminderSchedule newSchedule, DateTime nowUtc)
    {
        if (Status is not (ReminderStatus.Scheduled or ReminderStatus.Snoozed))
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(ChangeSchedule)));

        var previousFireAtUtc = Schedule.FireAtUtc;
        Schedule = newSchedule;
        Status = ReminderStatus.Scheduled;
        AddDomainEvent(new ReminderRescheduledDomainEvent(Id, TenantId, previousFireAtUtc, Schedule.FireAtUtc));
        return Result.Success();
    }

    /// <summary>Edición de texto. No toca el schedule, así que no reagenda nada en Quartz.</summary>
    public Result ChangeSubject(ReminderSubject newSubject)
    {
        if (Status is ReminderStatus.Dismissed or ReminderStatus.Cancelled or ReminderStatus.Missed)
            return Result.Failure(ReminderErrors.InvalidTransition(Status, nameof(ChangeSubject)));

        Subject = newSubject;
        return Result.Success();
    }

    private Result TransitionToMissed(DateTime nowUtc)
    {
        var expectedFireAtUtc = Schedule.FireAtUtc;
        Status = ReminderStatus.Missed;
        ResolvedAtUtc = nowUtc;
        AddDomainEvent(new ReminderMissedDomainEvent(Id, TenantId, expectedFireAtUtc, nowUtc));
        return Result.Success();
    }
}
