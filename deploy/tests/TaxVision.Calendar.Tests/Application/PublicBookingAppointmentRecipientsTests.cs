using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Calendar.Application.Appointments.DomainEvents;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Appointments.Events;
using TaxVision.Calendar.Domain.ValueObjects;
using Wolverine;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>El visitante de una demo recibe los correos de la demo, no los avisos genéricos de la cita.</summary>
public sealed class PublicBookingAppointmentRecipientsTests
{
    private static readonly DateTime Start = DateTime.UtcNow.AddDays(3).Date.AddHours(14);

    [Fact]
    public async Task The_visitor_of_a_public_demo_is_left_out_of_the_generic_notices()
    {
        var demo = Schedule(bookingPageId: Guid.NewGuid());

        var recipients = await RecipientsAsync(demo);

        Assert.Empty(recipients);
    }

    [Fact]
    public async Task An_external_guest_of_a_normal_appointment_still_gets_them()
    {
        var normal = Schedule(bookingPageId: null);

        var recipients = await RecipientsAsync(normal);

        Assert.Equal("jane@acme-tax.com", Assert.Single(recipients).Email);
    }

    private static Appointment Schedule(Guid? bookingPageId)
    {
        var visitor = new InitialAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Jane Doe", "jane@acme-tax.com").Value,
            IsRequired: true,
            Accepted: true
        );
        return Appointment
            .Schedule(
                Guid.NewGuid(),
                AppointmentTitle.Create("Live demo").Value,
                EventTiming.PointInTimeOf(Start, Start.AddMinutes(30), "America/New_York").Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                DateTime.UtcNow,
                attendees: [visitor],
                bookingPageId: bookingPageId
            )
            .Value;
    }

    private static async Task<IReadOnlyList<AppointmentRecipient>> RecipientsAsync(Appointment appointment)
    {
        var scheduled = appointment.DomainEvents.OfType<AppointmentScheduledDomainEvent>().Single();
        var messages = await AppointmentScheduledDomainEventHandler.Handle(
            scheduled,
            new InMemoryAppointments(appointment),
            new Envelope(),
            default
        );
        return messages.OfType<AppointmentScheduledIntegrationEvent>().Single().Recipients;
    }
}
