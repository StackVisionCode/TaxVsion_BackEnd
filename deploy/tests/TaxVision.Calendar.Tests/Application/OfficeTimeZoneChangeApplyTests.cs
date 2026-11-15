using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Appointments.Commands;
using TaxVision.Calendar.Application.Office.TimeZoneChanges;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.Office;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// P9: aplicar el cambio de zona mueve solo lo que el admin eligió, a la misma hora local en la zona nueva. Una serie ya
/// empezada se corta en su próxima ocurrencia (lo pasado queda igual); una cita puntual nunca se mueve.
/// </summary>
public sealed class OfficeTimeZoneChangeApplyTests
{
    private const string NewYork = "America/New_York";
    private const string Chicago = "America/Chicago";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Admin = Guid.NewGuid();

    // Sábado 14-nov-2026, 10:00 en Nueva York.
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Moves_the_chosen_hours_and_cuts_a_started_series_at_its_next_occurrence()
    {
        await using var db = NewDb();
        var chosenRule = Rule(Guid.NewGuid());
        var keptRule = Rule(Guid.NewGuid());
        var started = Weekly(new DateOnly(2026, 11, 2));
        var notStarted = Weekly(new DateOnly(2026, 12, 7));
        await SeedAsync(db, [chosenRule, keptRule], [started, notStarted]);
        var change = await RecordAsync(db);
        var bus = new FakeMessageBus();

        var result = await Apply(db, bus, change.Id, [chosenRule.Id], [started.Id, notStarted.Id]);

        Assert.True(result.IsSuccess);
        Assert.Equal(Chicago, chosenRule.TimeZone.Id);
        Assert.Equal(new TimeOnly(9, 0), chosenRule.StartTime);
        Assert.Equal(NewYork, keptRule.TimeZone.Id);

        var commands = bus.Published.OfType<RescheduleAppointmentCommand>().ToList();
        var cut = Assert.Single(commands, c => c.AppointmentId == started.Id);
        // Próxima ocurrencia: lunes 16-nov a las 9:00 de Nueva York (14:00Z).
        Assert.Equal(EditScope.ThisAndFollowing, cut.Scope);
        Assert.Equal(new DateTime(2026, 11, 16, 14, 0, 0, DateTimeKind.Utc), cut.OriginalStartUtc);
        Assert.Equal(new DateOnly(2026, 11, 16), cut.SeriesStartDate);
        Assert.Equal(new TimeOnly(9, 0), cut.LocalStartTime);
        Assert.Equal(Chicago, cut.TimeZoneId);
        var whole = Assert.Single(commands, c => c.AppointmentId == notStarted.Id);
        Assert.Equal(EditScope.EntireSeries, whole.Scope);
        Assert.Equal(new DateOnly(2026, 12, 7), whole.SeriesStartDate);

        Assert.Equal(OfficeTimeZoneChangeStatus.Applied, change.Status);
        Assert.Equal((1, 2), (change.MovedRules, change.MovedSeries));
        Assert.Equal("Calendar.OfficeTimeZoneChange.NotPending", (await Apply(db, bus, change.Id, [], [])).Error.Code);
    }

    [Fact]
    public async Task A_one_off_appointment_cannot_be_moved()
    {
        await using var db = NewDb();
        var oneOff = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Client meeting").Value,
                EventTiming.PointInTimeOf(Now.AddDays(3), Now.AddDays(3).AddHours(1), NewYork).Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            .Value;
        // Con un horario en la zona anterior hay algo que mover y el cambio se registra.
        await SeedAsync(db, [Rule(Guid.NewGuid())], [oneOff]);
        var change = await RecordAsync(db);

        var result = await Apply(db, new FakeMessageBus(), change.Id, [], [oneOff.Id]);

        Assert.Equal("Calendar.OfficeTimeZoneChange.NotMovable", result.Error.Code);
        Assert.Equal(OfficeTimeZoneChangeStatus.Pending, change.Status);
    }

    [Fact]
    public async Task Dismissing_moves_nothing()
    {
        await using var db = NewDb();
        var rule = Rule(Guid.NewGuid());
        await SeedAsync(db, [rule], []);
        var change = await RecordAsync(db);

        var result = await DismissOfficeTimeZoneChangeHandler.Handle(
            new DismissOfficeTimeZoneChangeCommand(Tenant, Admin, change.Id, Now),
            new OfficeTimeZoneChangeRepository(db),
            db,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(OfficeTimeZoneChangeStatus.Dismissed, change.Status);
        Assert.Equal(NewYork, rule.TimeZone.Id);
    }

    private static Task<BuildingBlocks.Results.Result> Apply(
        CalendarDbContext db,
        FakeMessageBus bus,
        Guid changeId,
        IReadOnlyList<Guid> rules,
        IReadOnlyList<Guid> series
    ) =>
        ApplyOfficeTimeZoneChangeHandler.Handle(
            new ApplyOfficeTimeZoneChangeCommand(Tenant, Admin, changeId, rules, series, Now),
            new OfficeTimeZoneChangeRepository(db),
            new OfficeTimeZoneCandidates(db),
            db,
            bus,
            CancellationToken.None
        );

    private static async Task<OfficeTimeZoneChange> RecordAsync(CalendarDbContext db)
    {
        var changes = new OfficeTimeZoneChangeRepository(db);
        await OfficeTimeZoneChangeConsumer.Handle(
            new TenantTimeZoneChangedIntegrationEvent
            {
                TenantId = Tenant,
                ChangedTenantId = Tenant,
                PreviousTimeZoneId = NewYork,
                NewTimeZoneId = Chicago,
                ChangedAtUtc = Now,
                TenantVersion = 2,
                CorrelationId = "c",
            },
            changes,
            new OfficeTimeZoneCandidates(db),
            db,
            CancellationToken.None
        );
        return (await changes.GetPendingAsync(Tenant))!;
    }

    private static async Task SeedAsync(CalendarDbContext db, AvailabilityRule[] rules, Appointment[] appointments)
    {
        db.AvailabilityRules.AddRange(rules);
        db.Appointments.AddRange(appointments);
        // En SQL el rowversion lo pone la base; en memoria hay que darle uno.
        foreach (var entry in db.ChangeTracker.Entries<Appointment>())
            entry.Property(x => x.RowVersion).CurrentValue = new byte[8];
        await db.SaveChangesAsync();
    }

    private static AvailabilityRule Rule(Guid userId) =>
        AvailabilityRule
            .Create(Tenant, userId, DaysOfWeekMask.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0), NewYork, Now)
            .Value;

    private static Appointment Weekly(DateOnly start)
    {
        var timing = EventTiming.RecurringOf(start, new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork).Value;
        var appointment = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Weekly review").Value,
                timing,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            .Value;
        Assert.True(
            appointment
                .MakeRecurring(
                    RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
                    timing,
                    appointment.OrganizerUserId,
                    canManageAll: false
                )
                .IsSuccess
        );
        return appointment;
    }

    private static CalendarDbContext NewDb() =>
        new(
            new DbContextOptionsBuilder<CalendarDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenant()
        );

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
