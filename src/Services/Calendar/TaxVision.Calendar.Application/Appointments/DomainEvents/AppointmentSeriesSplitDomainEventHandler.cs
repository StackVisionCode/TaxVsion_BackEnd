using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.Scheduling;
using Wolverine;

namespace TaxVision.Calendar.Application.Appointments.DomainEvents;

/// <summary>
/// «Esta y las siguientes»: nació una serie nueva. Los avisos del otro lado del corte los cierra
/// <see cref="AppointmentReminderTargetsClosedDomainEventHandler"/>.
/// </summary>
public static class AppointmentSeriesSplitDomainEventHandler
{
    public static async Task<OutgoingMessages> Handle(
        AppointmentSeriesSplitDomainEvent split,
        IAppointmentRepository appointments,
        Envelope envelope,
        CancellationToken ct
    )
    {
        var correlationId = envelope.CorrelationId ?? string.Empty;
        var messages = new OutgoingMessages
        {
            new CalendarSeriesSplitIntegrationEvent
            {
                TenantId = split.TenantId,
                CorrelationId = correlationId,
                OriginalSeriesId = split.OriginalAppointmentId,
                NewSeriesId = split.FollowerAppointmentId,
                CutoffUtc = split.CutOriginalStartUtc,
            },
        };

        var found = await appointments.GetByIdAsync(split.TenantId, split.FollowerAppointmentId, ct);
        if (found.IsFailure)
            return messages;

        // A los asistentes hay que decirles que su reunion cambio de hora desde el corte. Publicarlo
        // solo como «serie partida» no le sirve a nadie: eso es vocabulario nuestro.
        var follower = found.Value;
        var cut = split.CutOriginalStartUtc;
        var firstStart = OccurrenceExpander.FirstStart(follower) ?? cut;
        messages.Add(
            AppointmentEvents.Rescheduled(
                follower,
                cut,
                firstStart,
                firstStart.Add(follower.Timing.Duration ?? TimeSpan.FromHours(1)),
                follower.Timing.TimeZone.Id,
                nameof(EditScope.ThisAndFollowing),
                cut,
                correlationId
            )
        );

        // La serie nueva es la misma reunion: sus avisos se piden ya, con ids propios.
        messages.AddRange(AppointmentReminderRequests.For(follower, split.SplitAtUtc, correlationId));
        return messages;
    }
}
