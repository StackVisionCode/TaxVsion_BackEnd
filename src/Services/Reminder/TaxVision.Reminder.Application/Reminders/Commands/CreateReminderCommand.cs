using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;

namespace TaxVision.Reminder.Application.Reminders.Commands;

/// <summary>
/// <c>AnchorAtUtc</c> + <c>LeadMinutes</c> juntos producen un schedule <b>anclado</b> («30 min antes
/// de la cita»); <c>FireAtUtc</c> solo, uno <b>absoluto</b>. No se aceptan los tres: son dos formas
/// distintas de decir cuándo, y elegir por el emisor evitaría que el dominio pudiera contradecirse.
/// </summary>
public sealed record CreateReminderCommand(
    Guid TenantId,
    Guid UserId,
    string? Title,
    string? Body,
    ReminderCategory Category,
    Guid? TargetId,
    DateTime? FireAtUtc,
    DateTime? AnchorAtUtc,
    int? LeadMinutes,
    string? TimeZone,
    string? RequestKey
);

/// <summary>
/// SRP (guardrail #1): <c>Validate → Mutate → Persist → Schedule</c>.
///
/// <para>
/// <b>Idempotencia (ADR-R-07).</b> Dos capas: la consulta previa por <c>RequestKey</c> resuelve el
/// reintento normal, y el <c>catch</c> resuelve la carrera real de dos peticiones simultáneas que
/// pasan la consulta a la vez. La segunda choca contra el índice único
/// <c>(TenantId, RequestKey)</c> y devuelve el recordatorio que ganó — nunca un 500.
/// </para>
///
/// <para>
/// <b>Se atrapa <see cref="ConflictException"/>, no <c>DbUpdateException</c>.</b>
/// <c>ReminderDbContext.SaveChangesAsync</c> ya traduce el <c>SqlException</c> 2601/2627, y
/// <c>ConflictException</c> <b>no</b> hereda de <c>DbUpdateException</c>: atrapar el segundo no
/// capturaría nada y la carrera saldría como 500.
/// </para>
///
/// <para>
/// <b>EF primero, Quartz después</b> (ADR-R-04): si agendar falla, queda un recordatorio sin trigger
/// que <c>ReminderScheduleReconciliationJob</c> repara en ≤5 min. Al revés quedaría un trigger
/// apuntando a una fila inexistente, que no repara nadie.
/// </para>
/// </summary>
public static class CreateReminderHandler
{
    public static async Task<Result<ReminderResponse>> Handle(
        CreateReminderCommand command,
        IReminderRepository reminders,
        IReminderScheduler scheduler,
        IUnitOfWork unitOfWork,
        IReminderMetrics metrics,
        ILogger<ReminderAggregate> logger,
        CancellationToken ct
    )
    {
        var requestKey = RequestKey.Create(command.RequestKey);
        if (requestKey.IsFailure)
            return Result.Failure<ReminderResponse>(requestKey.Error);

        var existing = await reminders.FindByRequestKeyAsync(command.TenantId, requestKey.Value, ct);
        if (existing is not null)
        {
            metrics.RecordDuplicateSuppressed(ReminderDuplicateResolutions.Lookup);
            return Result.Success(ReminderResponse.From(existing));
        }

        var nowUtc = DateTime.UtcNow;
        var buildResult = Build(command, requestKey.Value, nowUtc);
        if (buildResult.IsFailure)
            return Result.Failure<ReminderResponse>(buildResult.Error);

        var reminder = buildResult.Value;
        reminders.Add(reminder);

        try
        {
            await unitOfWork.SaveChangesAsync(ct);
        }
        catch (ConflictException)
        {
            var winner = await reminders.FindByRequestKeyAsync(command.TenantId, requestKey.Value, ct);
            if (winner is null)
                return Result.Failure<ReminderResponse>(ReminderErrors.DuplicateRequest);

            metrics.RecordDuplicateSuppressed(ReminderDuplicateResolutions.UniqueIndexRace);
            logger.LogInformation(
                "Concurrent create with request key {RequestKey} for tenant {TenantId}; returning the winner {ReminderId}.",
                requestKey.Value.Value,
                command.TenantId,
                winner.Id
            );
            return Result.Success(ReminderResponse.From(winner));
        }

        await scheduler.ScheduleAsync(reminder.TenantId, reminder.Id, reminder.Schedule.FireAtUtc, ct);

        // Solo el alta cuenta como "agendado". Snooze y reschedule reagendan un recordatorio que ya
        // se contó: sumarlos acá rompería la razón fired/scheduled, que es para lo que sirve el par.
        metrics.RecordScheduled(reminder.Target.Category);
        return Result.Success(ReminderResponse.From(reminder));
    }

    private static Result<ReminderAggregate> Build(CreateReminderCommand command, RequestKey requestKey, DateTime nowUtc)
    {
        var subject = ReminderSubject.Create(command.Title, command.Body);
        if (subject.IsFailure)
            return Result.Failure<ReminderAggregate>(subject.Error);

        var target = ReminderTarget.Create(command.Category, command.TargetId);
        if (target.IsFailure)
            return Result.Failure<ReminderAggregate>(target.Error);

        var schedule = BuildSchedule(command, nowUtc);
        if (schedule.IsFailure)
            return Result.Failure<ReminderAggregate>(schedule.Error);

        var timeZone = ReminderTimeZone.Create(command.TimeZone);
        if (timeZone.IsFailure)
            return Result.Failure<ReminderAggregate>(timeZone.Error);

        return ReminderAggregate.Create(
            command.TenantId,
            command.UserId,
            subject.Value,
            target.Value,
            schedule.Value,
            timeZone.Value,
            requestKey,
            nowUtc
        );
    }

    private static Result<ReminderSchedule> BuildSchedule(CreateReminderCommand command, DateTime nowUtc) =>
        command is { AnchorAtUtc: { } anchor, LeadMinutes: { } lead }
            ? ReminderSchedule.Anchored(anchor, lead, nowUtc)
            : ReminderSchedule.Absolute(command.FireAtUtc ?? default, nowUtc);
}
