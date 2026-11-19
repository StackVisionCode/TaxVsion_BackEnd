using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using TaxVision.Tenant.Application.Team.Consumers;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Tests.TestSupport;

namespace TaxVision.Tenant.Tests.Application.Team;

public sealed class CalendarActivityConsumerTests : ActivityConsumerContract
{
    private static readonly Guid Actor = Guid.NewGuid();
    private static readonly Guid AppointmentId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    // El cliente de la cita viaja entre los destinatarios; el compañero (UserId) no cuenta como cliente.
    private static readonly IReadOnlyList<AppointmentRecipient> Recipients =
    [
        new("ana@office.test", Guid.NewGuid()),
        new("client@mail.test", null, CustomerId),
    ];

    protected override IEnumerable<ActivityCase> Cases()
    {
        var rescheduled = Stamp(
            new AppointmentRescheduledIntegrationEvent
            {
                AppointmentId = AppointmentId,
                Scope = "Single",
                NewStartUtc = DateTime.UtcNow,
                NewEndUtc = DateTime.UtcNow.AddHours(1),
                TimeZoneId = "America/Chicago",
                OrganizerUserId = Guid.NewGuid(),
                Recipients = Recipients,
                ChangedByUserId = Actor,
            }
        );
        yield return new(
            rescheduled,
            rescheduled with
            {
                ChangedByUserId = null,
            },
            (e, r, u, c) =>
                CalendarActivityConsumer.Handle((AppointmentRescheduledIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.AppointmentRescheduled,
            AppointmentId,
            CustomerId,
            ActivityVisibility.Team
        );

        var cancelled = Stamp(
            new AppointmentCancelledIntegrationEvent
            {
                AppointmentId = AppointmentId,
                Scope = "Series",
                ChangedByUserId = Actor,
            }
        );
        yield return new(
            cancelled,
            cancelled with
            {
                ChangedByUserId = null,
            },
            (e, r, u, c) => CalendarActivityConsumer.Handle((AppointmentCancelledIntegrationEvent)e, r, u, c, default),
            Actor,
            ActivityVerbs.AppointmentCancelled,
            AppointmentId,
            null,
            ActivityVisibility.Team
        );
    }

    [Fact]
    public async Task The_organizer_is_never_taken_as_the_actor()
    {
        var harness = new ActivityHarness();
        var evt = Stamp(
            new AppointmentCancelledIntegrationEvent
            {
                AppointmentId = AppointmentId,
                Scope = "Single",
                OrganizerUserId = Guid.NewGuid(),
            }
        );

        await CalendarActivityConsumer.Handle(evt, harness, harness, harness, default);

        Assert.Empty(harness.Saved);
    }
}
