using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.Commands;

/// <summary>
/// Los tres alcances hacen cosas distintas de verdad, no variantes de una misma: uno crea una
/// excepción, otro parte la serie y el tercero muta la regla. Van aparte del handler por el
/// presupuesto de líneas.
/// </summary>
internal static class RescheduleApplier
{
    public static async Task<Result<Appointment>> ApplyAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        if (!appointment.IsRecurring)
            return await OneOffAsync(appointment, command, bus, correlation);

        if (command.Scope is not { } scope)
            return Result.Failure<Appointment>(AppointmentErrors.ScopeRequired);

        return scope switch
        {
            EditScope.ThisOccurrence => await OccurrenceAsync(appointment, command, bus, correlation),
            EditScope.ThisAndFollowing => await FollowingAsync(appointment, command, appointments, bus, correlation),
            _ => await EntireSeriesAsync(appointment, command, bus, correlation),
        };
    }

    private static async Task<Result<Appointment>> OneOffAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var timing = PointInTime(command, appointment);
        if (timing.IsFailure)
            return Result.Failure<Appointment>(timing.Error);

        var previous = appointment.Timing.StartUtc;
        var moved = appointment.Reschedule(timing.Value, command.ActingUserId, DateTime.UtcNow);
        if (moved.IsFailure)
            return Result.Failure<Appointment>(moved.Error);

        await PublishMovedAsync(
            appointment,
            previous,
            timing.Value.StartUtc!.Value,
            timing.Value.EndUtc!.Value,
            timing.Value.TimeZone.Id,
            nameof(EditScope.EntireSeries),
            null,
            bus,
            correlation
        );
        return Result.Success(appointment);
    }

    private static async Task<Result<Appointment>> OccurrenceAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IMessageBus bus,
        ICorrelationContext correlation
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
            DateTime.UtcNow
        );

        if (overridden.IsFailure)
            return Result.Failure<Appointment>(overridden.Error);

        var moved = command.NewStartUtc ?? original;
        await PublishMovedAsync(
            appointment,
            original,
            moved,
            command.NewEndUtc ?? moved.Add(appointment.Timing.Duration ?? TimeSpan.FromHours(1)),
            appointment.Timing.TimeZone.Id,
            nameof(EditScope.ThisOccurrence),
            original,
            bus,
            correlation
        );

        return Result.Success(appointment);
    }

    /// <summary>El mismo horizonte que usa <c>ReminderScheduleJob</c>: mas alla no hay avisos pedidos.</summary>
    private const int ReminderHorizonDays = 60;

    private static async Task<Result<Appointment>> FollowingAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IAppointmentRepository appointments,
        IMessageBus bus,
        ICorrelationContext correlation
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
        var orphaned = OccurrenceExpander.Expand(appointment, cut, cut.AddDays(ReminderHorizonDays));

        var follower = appointment.SplitForFollowing(
            cut,
            timing.Value,
            rule.Value,
            command.ActingUserId,
            DateTime.UtcNow
        );
        if (follower.IsFailure)
            return Result.Failure<Appointment>(follower.Error);

        appointments.Add(follower.Value);

        await bus.PublishAsync(
            new CalendarSeriesSplitIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
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
            foreach (var occurrence in orphaned.Value)
            {
                await bus.PublishAsync(
                    new ReminderTargetClosedIntegrationEvent
                    {
                        TenantId = appointment.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        Category = "Calendar",
                        TargetId = OccurrenceTargetId.For(appointment.Id, occurrence.OriginalStartUtc),
                        Reason = "series split",
                    }
                );
            }
        }

        // Y a los asistentes hay que decirles que su reunion cambio de hora desde el corte. Publicarlo
        // solo como «serie partida» no le sirve a nadie: eso es vocabulario nuestro.
        await PublishMovedAsync(
            follower.Value,
            cut,
            OccurrenceExpander.FirstStart(follower.Value) ?? cut,
            (OccurrenceExpander.FirstStart(follower.Value) ?? cut).Add(timing.Value.Duration ?? TimeSpan.FromHours(1)),
            timing.Value.TimeZone.Id,
            nameof(EditScope.ThisAndFollowing),
            cut,
            bus,
            correlation
        );

        return Result.Success(follower.Value);
    }

    private static async Task<Result<Appointment>> EntireSeriesAsync(
        Appointment appointment,
        RescheduleAppointmentCommand command,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var timing = SeriesTiming(command, appointment);
        if (timing.IsFailure)
            return Result.Failure<Appointment>(timing.Error);

        var rule = RecurrenceRule.Create(command.RecurrenceRule ?? appointment.Recurrence!.Value);
        if (rule.IsFailure)
            return Result.Failure<Appointment>(rule.Error);

        var first = OccurrenceExpander.FirstStart(appointment);
        var edited = appointment.EditEntireSeries(timing.Value, rule.Value, command.ActingUserId, DateTime.UtcNow);
        if (edited.IsFailure)
            return Result.Failure<Appointment>(edited.Error);

        var newFirst = OccurrenceExpander.FirstStart(appointment) ?? DateTime.UtcNow;
        await PublishMovedAsync(
            appointment,
            first,
            newFirst,
            newFirst.Add(timing.Value.Duration ?? TimeSpan.FromHours(1)),
            timing.Value.TimeZone.Id,
            nameof(EditScope.EntireSeries),
            null,
            bus,
            correlation
        );

        return Result.Success(appointment);
    }

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
    /// Mover la cita sin avisar a Reminder deja el recordatorio en la hora vieja. Es el bug que el
    /// usuario reporta como «me avisó tarde» y que nadie encuentra, porque Calendar funciona.
    /// </summary>
    private static async Task PublishMovedAsync(
        Appointment appointment,
        DateTime? previousStartUtc,
        DateTime newStartUtc,
        DateTime newEndUtc,
        string timeZoneId,
        string scope,
        DateTime? originalStartUtc,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        await bus.PublishAsync(
            new AppointmentRescheduledIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
                AppointmentId = appointment.Id,
                Scope = scope,
                OriginalStartUtc = originalStartUtc,
                PreviousStartUtc = previousStartUtc,
                NewStartUtc = newStartUtc,
                NewEndUtc = newEndUtc,
                TimeZoneId = timeZoneId,
                MeetingId = appointment.MeetingId,
                Recipients = AppointmentEvents.RecipientsOf(appointment),
            }
        );

        await bus.PublishAsync(
            new ReminderTargetMovedIntegrationEvent
            {
                TenantId = appointment.TenantId,
                CorrelationId = correlation.CorrelationId,
                Category = "Calendar",
                TargetId = OccurrenceTargetId.For(
                    appointment.Id,
                    originalStartUtc ?? previousStartUtc ?? DateTime.UtcNow
                ),
                NewAnchorAtUtc = newStartUtc,
            }
        );
    }
}
