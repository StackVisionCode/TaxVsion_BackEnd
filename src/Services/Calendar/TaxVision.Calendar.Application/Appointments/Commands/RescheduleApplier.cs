using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>
/// Los tres alcances hacen cosas distintas de verdad, no variantes de una misma: uno crea una
/// excepción, otro parte la serie y el tercero muta la regla. Van aparte del handler por el
/// presupuesto de líneas. No publica: deja los mensajes en <c>outgoing</c> y el handler los
/// publica después de guardar.
/// </summary>
internal static class RescheduleApplier
{
    public static Result<Appointment> Apply(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        string correlationId,
        List<object> outgoing
    )
    {
        if (!appointment.IsRecurring)
            return OneOff(appointment, command, correlationId, outgoing);

        if (command.Scope is not { } scope)
            return Result.Failure<Appointment>(AppointmentErrors.ScopeRequired);

        return scope switch
        {
            EditScope.ThisOccurrence => Occurrence(appointment, command, correlationId, outgoing),
            EditScope.ThisAndFollowing => Following(appointment, command, appointments, correlationId, outgoing),
            _ => EntireSeries(appointment, command, correlationId, outgoing),
        };
    }

    private static Result<Appointment> OneOff(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        string correlationId,
        List<object> outgoing
    )
    {
        var timing = PointInTime(command, appointment);
        if (timing.IsFailure)
            return Result.Failure<Appointment>(timing.Error);

        var now = DateTime.UtcNow;
        var before = AppointmentReminderTargets.Snapshot(appointment, now);
        var previousLead = appointment.ReminderLeadMinutes;
        var previousStart = appointment.Timing.StartUtc;

        var moved = appointment.Reschedule(timing.Value, command.ActingUserId, command.CanManageAll, now);
        if (moved.IsFailure)
            return Result.Failure<Appointment>(moved.Error);

        var lead = ApplyLead(appointment, command);
        if (lead.IsFailure)
            return Result.Failure<Appointment>(lead.Error);

        outgoing.Add(
            Rescheduled(
                appointment,
                previousStart,
                timing.Value.StartUtc!.Value,
                timing.Value.EndUtc!.Value,
                timing.Value.TimeZone.Id,
                nameof(EditScope.EntireSeries),
                null,
                correlationId
            )
        );
        outgoing.AddRange(AppointmentReminderTargets.AfterChange(appointment, before, previousLead, now, correlationId));
        return Result.Success(appointment);
    }

    /// <summary>La ocurrencia conserva su id (inicio original): basta con mover el ancla del aviso.</summary>
    private static Result<Appointment> Occurrence(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        string correlationId,
        List<object> outgoing
    )
    {
        if (command.OriginalStartUtc is not { } original)
            return Result.Failure<Appointment>(RecurrenceErrors.NotAnOccurrence);

        var overridden = appointment.OverrideOccurrence(
            original,
            command.NewStartUtc,
            command.NewEndUtc,
            null,
            null,
            command.ActingUserId,
            command.CanManageAll,
            DateTime.UtcNow
        );

        if (overridden.IsFailure)
            return Result.Failure<Appointment>(overridden.Error);

        var moved = command.NewStartUtc ?? original;
        outgoing.Add(
            Rescheduled(
                appointment,
                original,
                moved,
                command.NewEndUtc ?? moved.Add(appointment.Timing.Duration ?? TimeSpan.FromHours(1)),
                appointment.Timing.TimeZone.Id,
                nameof(EditScope.ThisOccurrence),
                original,
                correlationId
            )
        );
        outgoing.Add(
            new ReminderTargetMovedIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlationId,
                Category = AppointmentReminderRequests.Category,
                TargetId = OccurrenceTargetId.For(appointment.Id, original),
                NewAnchorAtUtc = moved,
            }
        );

        return Result.Success(appointment);
    }

    private static Result<Appointment> Following(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        string correlationId,
        List<object> outgoing
    )
    {
        if (command.OriginalStartUtc is not { } cut)
            return Result.Failure<Appointment>(RecurrenceErrors.NotAnOccurrence);

        var timing = SeriesTiming(command, appointment);
        if (timing.IsFailure)
            return Result.Failure<Appointment>(timing.Error);

        var rule = RecurrenceRule.Create(command.RecurrenceRule ?? appointment.Recurrence!.Value);
        if (rule.IsFailure)
            return Result.Failure<Appointment>(rule.Error);

        // Hay que mirar las ocurrencias de la serie vieja ANTES de partirla: el corte le pone UNTIL,
        // y despues ya no hay forma de saber que avisos habia pedido para el otro lado.
        var orphaned = OccurrenceExpander.Expand(appointment, cut, cut.AddDays(AppointmentReminderRequests.HorizonDays));

        var now = DateTime.UtcNow;
        var follower = appointment.SplitForFollowing(
            cut,
            timing.Value,
            rule.Value,
            command.ActingUserId,
            now,
            command.CanManageAll
        );
        if (follower.IsFailure)
            return Result.Failure<Appointment>(follower.Error);

        var lead = ApplyLead(follower.Value, command);
        if (lead.IsFailure)
            return Result.Failure<Appointment>(lead.Error);

        appointments.Add(follower.Value);

        outgoing.Add(
            new CalendarSeriesSplitIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlationId,
                OriginalSeriesId = appointment.Id,
                NewSeriesId = follower.Value.Id,
                CutoffUtc = cut,
            }
        );

        // Los avisos del otro lado del corte quedaron apuntando a una serie que ya no llega hasta
        // ahi. Sin cerrarlos, el recordatorio viejo suena a la hora vieja y el de la serie nueva
        // suena tambien: dos avisos para la misma reunion.
        if (orphaned.IsSuccess)
        {
            outgoing.AddRange(
                AppointmentReminderTargets.Close(
                    appointment,
                    orphaned.Value.Select(o => OccurrenceTargetId.For(appointment.Id, o.OriginalStartUtc)),
                    correlationId,
                    "series split"
                )
            );
        }

        // Y a los asistentes hay que decirles que su reunion cambio de hora desde el corte. Publicarlo
        // solo como «serie partida» no le sirve a nadie: eso es vocabulario nuestro.
        var firstStart = OccurrenceExpander.FirstStart(follower.Value) ?? cut;
        outgoing.Add(
            Rescheduled(
                follower.Value,
                cut,
                firstStart,
                firstStart.Add(timing.Value.Duration ?? TimeSpan.FromHours(1)),
                timing.Value.TimeZone.Id,
                nameof(EditScope.ThisAndFollowing),
                cut,
                correlationId
            )
        );

        // La serie nueva es la misma reunion: sus avisos se piden ya, con ids propios.
        outgoing.AddRange(AppointmentReminderRequests.For(follower.Value, now, correlationId));

        return Result.Success(follower.Value);
    }

    private static Result<Appointment> EntireSeries(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        string correlationId,
        List<object> outgoing
    )
    {
        var timing = SeriesTiming(command, appointment);
        if (timing.IsFailure)
            return Result.Failure<Appointment>(timing.Error);

        var rule = RecurrenceRule.Create(command.RecurrenceRule ?? appointment.Recurrence!.Value);
        if (rule.IsFailure)
            return Result.Failure<Appointment>(rule.Error);

        var now = DateTime.UtcNow;
        var before = AppointmentReminderTargets.Snapshot(appointment, now);
        var previousLead = appointment.ReminderLeadMinutes;
        var first = OccurrenceExpander.FirstStart(appointment);

        var edited = appointment.EditEntireSeries(
            timing.Value,
            rule.Value,
            command.ActingUserId,
            command.CanManageAll,
            now
        );
        if (edited.IsFailure)
            return Result.Failure<Appointment>(edited.Error);

        var lead = ApplyLead(appointment, command);
        if (lead.IsFailure)
            return Result.Failure<Appointment>(lead.Error);

        var newFirst = OccurrenceExpander.FirstStart(appointment) ?? now;
        outgoing.Add(
            Rescheduled(
                appointment,
                first,
                newFirst,
                newFirst.Add(timing.Value.Duration ?? TimeSpan.FromHours(1)),
                timing.Value.TimeZone.Id,
                nameof(EditScope.EntireSeries),
                null,
                correlationId
            )
        );
        outgoing.AddRange(AppointmentReminderTargets.AfterChange(appointment, before, previousLead, now, correlationId));

        return Result.Success(appointment);
    }

    /// <summary>Sin minutos en el pedido el aviso queda como estaba; cero lo quita.</summary>
    private static Result ApplyLead(Appointment appointment, RescheduleAppointmentCommand command) =>
        command.ReminderLeadMinutes is { } lead ? appointment.RequestReminder(lead) : Result.Success();

    /// <summary>
    /// Sin zona en el pedido se queda la que la cita ya tenía: mover una cita no la cambia de zona, y
    /// obligar a repetirla convierte en 400 el caso más común.
    /// </summary>
    private static Result<EventTiming> PointInTime(RescheduleAppointmentCommand command, Appointment appointment)
    {
        if (command.NewStartUtc is not { } start || command.NewEndUtc is not { } end)
            return Result.Failure<EventTiming>(TimingErrors.EndBeforeStart);

        return EventTiming.PointInTimeOf(start, end, command.TimeZoneId ?? appointment.Timing.TimeZone.Id);
    }

    private static Result<EventTiming> SeriesTiming(RescheduleAppointmentCommand command, Appointment appointment)
    {
        if (command.SeriesStartDate is not { } start || command.LocalStartTime is not { } time)
            return Result.Failure<EventTiming>(TimingErrors.RecurringMustBeLocal);

        return EventTiming.RecurringOf(
            start,
            time,
            command.Duration ?? TimeSpan.FromHours(1),
            command.TimeZoneId ?? appointment.Timing.TimeZone.Id
        );
    }

    /// <summary>
    /// Mover la cita sin avisar a los asistentes los deja presentándose a la hora vieja. Reminder se
    /// entera aparte, por objetivo cerrado y pedido nuevo.
    /// </summary>
    private static AppointmentRescheduledIntegrationEvent Rescheduled(
        Appointment appointment,
        DateTime? previousStartUtc,
        DateTime newStartUtc,
        DateTime newEndUtc,
        string timeZoneId,
        string scope,
        DateTime? originalStartUtc,
        string correlationId
    ) =>
        new()
        {
            TenantId = appointment.TenantId,
            CorrelationId = correlationId,
            AppointmentId = appointment.Id,
            Scope = scope,
            OriginalStartUtc = originalStartUtc,
            PreviousStartUtc = previousStartUtc,
            NewStartUtc = newStartUtc,
            NewEndUtc = newEndUtc,
            TimeZoneId = timeZoneId,
            MeetingId = appointment.MeetingId,
            Recipients = AppointmentEvents.RecipientsOf(appointment),
        };
}
