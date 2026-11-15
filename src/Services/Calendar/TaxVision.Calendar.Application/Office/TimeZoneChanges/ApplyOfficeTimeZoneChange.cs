using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.TimeZones;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Calendar.Application.Office.TimeZoneChanges;

public sealed record ApplyOfficeTimeZoneChangeCommand(
    Guid TenantId,
    Guid ActingUserId,
    Guid ChangeId,
    IReadOnlyList<Guid> RuleIds,
    IReadOnlyList<Guid> SeriesIds,
    DateTime NowUtc
);

public sealed record DismissOfficeTimeZoneChangeCommand(
    Guid TenantId,
    Guid ActingUserId,
    Guid ChangeId,
    DateTime NowUtc
);

/// <summary>
/// P9: mueve a la zona nueva solo lo elegido, a la misma hora local. Los horarios cambian aquí; cada serie pasa por el
/// reprogramar de siempre (avisos, recordatorios y sala), una por mensaje: la ya empezada se corta en su próxima
/// ocurrencia y lo pasado queda igual; la que no empezó se mueve entera. Una cita puntual nunca entra.
/// </summary>
public static class ApplyOfficeTimeZoneChangeHandler
{
    private static readonly TimeSpan Horizon = TimeSpan.FromDays(730);

    public static async Task<Result> Handle(
        ApplyOfficeTimeZoneChangeCommand command,
        IOfficeTimeZoneChangeRepository changes,
        IOfficeTimeZoneCandidates candidates,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var change = await changes.GetPendingAsync(command.TenantId, ct);
        if (change is null || change.Id != command.ChangeId)
            return Result.Failure(OfficeTimeZoneChangeErrors.NotPending);

        var rules = (await candidates.ActiveRulesInZoneAsync(command.TenantId, change.FromTimeZoneId, ct)).ToDictionary(
            r => r.Id
        );
        var series = (await candidates.LiveSeriesInZoneAsync(command.TenantId, change.FromTimeZoneId, ct)).ToDictionary(
            a => a.Id
        );
        if (command.RuleIds.Any(id => !rules.ContainsKey(id)))
            return Result.Failure(OfficeTimeZoneChangeErrors.NotMovable);

        var reschedules = new List<RescheduleAppointmentCommand>();
        foreach (var id in command.SeriesIds.Distinct())
        {
            if (
                !series.TryGetValue(id, out var appointment)
                || Reschedule(appointment, change, command) is not { } reschedule
            )
                return Result.Failure(OfficeTimeZoneChangeErrors.NotMovable);
            reschedules.Add(reschedule);
        }

        var target = CalendarTimeZone.Create(change.ToTimeZoneId).Value;
        foreach (var id in command.RuleIds.Distinct())
            rules[id].MoveToZone(target);

        var applied = change.Apply(
            command.ActingUserId,
            command.RuleIds.Distinct().Count(),
            reschedules.Count,
            command.NowUtc
        );
        if (applied.IsFailure)
            return applied;

        // Guardar antes de publicar: si el guardado falla, ninguna serie se mueve.
        await unitOfWork.SaveChangesAsync(ct);
        foreach (var reschedule in reschedules)
            await bus.PublishAsync(reschedule);
        return Result.Success();
    }

    private static RescheduleAppointmentCommand? Reschedule(
        Appointment appointment,
        OfficeTimeZoneChange change,
        ApplyOfficeTimeZoneChangeCommand command
    )
    {
        var upcoming = OccurrenceExpander.Expand(appointment, command.NowUtc, command.NowUtc + Horizon);
        if (upcoming.IsFailure || upcoming.Value.Count == 0 || appointment.Timing.LocalStartTime is not { } localTime)
            return null;

        var cut = upcoming.Value.Min(o => o.OriginalStartUtc);
        var wholeSeries = appointment.IsFirstOccurrence(cut);
        var startDate = wholeSeries
            ? appointment.Timing.SeriesStartDate
            : CivilDate.Of(cut, ResolvedTimeZone.Resolve(appointment.Timing.TimeZone.Id));

        return new RescheduleAppointmentCommand(
            command.TenantId,
            appointment.Id,
            command.ActingUserId,
            wholeSeries ? EditScope.EntireSeries : EditScope.ThisAndFollowing,
            wholeSeries ? null : cut,
            null,
            null,
            startDate,
            localTime,
            appointment.Timing.Duration,
            change.ToTimeZoneId,
            null,
            CanManageAll: true
        );
    }
}

/// <summary>P9: el admin decide no mover nada; el cambio queda cerrado.</summary>
public static class DismissOfficeTimeZoneChangeHandler
{
    public static async Task<Result> Handle(
        DismissOfficeTimeZoneChangeCommand command,
        IOfficeTimeZoneChangeRepository changes,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var change = await changes.GetPendingAsync(command.TenantId, ct);
        if (change is null || change.Id != command.ChangeId)
            return Result.Failure(OfficeTimeZoneChangeErrors.NotPending);

        var dismissed = change.Dismiss(command.ActingUserId, command.NowUtc);
        if (dismissed.IsFailure)
            return dismissed;
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
