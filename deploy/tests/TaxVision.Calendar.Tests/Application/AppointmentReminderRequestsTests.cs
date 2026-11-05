using TaxVision.Calendar.Application.Appointments;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// El pedido a Reminder va al organizador VIGENTE con una clave estable por ocurrencia: así una
/// reasignación crea el aviso del nuevo (Reminder deduplica por destinatario) sin duplicar el resto.
/// </summary>
public sealed class AppointmentReminderRequestsTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static Appointment PointInTime(Guid organizer, int? leadMinutes)
    {
        var start = Now.AddDays(3);
        var timing = EventTiming.PointInTimeOf(start, start.AddHours(1), "America/New_York").Value;
        var appointment = Appointment
            .Schedule(Tenant, AppointmentTitle.Create("Client review").Value, timing, Guid.NewGuid(), organizer, Now)
            .Value;
        appointment.RequestReminder(leadMinutes);
        return appointment;
    }

    [Fact]
    public void Requests_one_reminder_per_occurrence_for_the_current_organizer()
    {
        var organizer = Guid.NewGuid();
        var appointment = PointInTime(organizer, leadMinutes: 15);

        var request = Assert.Single(AppointmentReminderRequests.For(appointment, Now, "corr"));

        var expectedTarget = OccurrenceTargetId.For(appointment.Id, appointment.Timing.StartUtc!.Value);
        Assert.Equal(organizer, request.UserId);
        Assert.Equal(expectedTarget, request.TargetId);
        Assert.Equal($"calendar:{expectedTarget:N}", request.RequestKey); // clave histórica: no cambiar
        Assert.Equal(15, request.LeadMinutes);
        Assert.Equal("corr", request.CorrelationId);
    }

    [Fact]
    public void After_reassignment_the_same_key_goes_to_the_new_organizer()
    {
        var appointment = PointInTime(Guid.NewGuid(), leadMinutes: 15);
        var before = Assert.Single(AppointmentReminderRequests.For(appointment, Now, "corr"));
        var successor = Guid.NewGuid();

        appointment.ReassignOrganizer(successor);
        var after = Assert.Single(AppointmentReminderRequests.For(appointment, Now, "corr"));

        Assert.Equal(before.RequestKey, after.RequestKey);
        Assert.Equal(successor, after.UserId);
    }

    [Fact]
    public void RequestKey_GenerationZero_KeepsLegacyFormat()
    {
        var target = Guid.NewGuid();

        Assert.Equal($"calendar:{target:N}", AppointmentReminderRequests.RequestKey(target, 0));
        Assert.EndsWith(":g2", AppointmentReminderRequests.RequestKey(target, 2));
    }

    [Fact]
    public void Without_lead_minutes_there_is_nothing_to_request()
    {
        Assert.Empty(AppointmentReminderRequests.For(PointInTime(Guid.NewGuid(), leadMinutes: null), Now, "corr"));
    }

    [Fact]
    public void A_cancelled_appointment_requests_nothing()
    {
        var organizer = Guid.NewGuid();
        var appointment = PointInTime(organizer, leadMinutes: 15);
        appointment.Cancel(organizer, canManageAll: false, reason: null, Now);

        Assert.Empty(AppointmentReminderRequests.For(appointment, Now, "corr"));
    }
}
