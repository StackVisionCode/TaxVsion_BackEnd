using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
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
/// P9: Calendar registra el cambio de zona de la oficina y arma la vista previa de lo que se puede mover: los horarios
/// de los preparadores y las series que estaban en la zona anterior. Las citas puntuales nunca entran.
/// </summary>
public sealed class OfficeTimeZoneChangeTests
{
    private const string NewYork = "America/New_York";
    private const string Chicago = "America/Chicago";
    private const string Denver = "America/Denver";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_second_change_replaces_the_pending_one_and_keeps_the_original_zone()
    {
        var first = OfficeTimeZoneChange.Record(Tenant, NewYork, Chicago, Now, 2, null, Now).Value!;

        var second = OfficeTimeZoneChange
            .Record(Tenant, Chicago, Denver, Now.AddHours(1), 3, first, Now.AddHours(1))
            .Value!;

        Assert.Equal(OfficeTimeZoneChangeStatus.Superseded, first.Status);
        Assert.Equal(NewYork, second.FromTimeZoneId);
        Assert.Equal(Denver, second.ToTimeZoneId);
        Assert.Equal(OfficeTimeZoneChangeStatus.Pending, second.Status);
    }

    [Fact]
    public void Going_back_to_the_original_zone_leaves_nothing_pending()
    {
        var first = OfficeTimeZoneChange.Record(Tenant, NewYork, Chicago, Now, 2, null, Now).Value!;

        var back = OfficeTimeZoneChange.Record(Tenant, Chicago, NewYork, Now.AddHours(1), 3, first, Now.AddHours(1));

        Assert.True(back.IsSuccess);
        Assert.Null(back.Value);
        Assert.Equal(OfficeTimeZoneChangeStatus.Superseded, first.Status);
    }

    [Fact]
    public async Task The_preview_lists_hours_and_series_of_the_previous_zone_and_never_one_off_appointments()
    {
        await using var db = NewDb();
        var preparer = Guid.NewGuid();
        var nyRule = Rule(preparer, NewYork);
        var inactiveNyRule = Rule(Guid.NewGuid(), NewYork);
        inactiveNyRule.Deactivate();
        db.AvailabilityRules.AddRange(nyRule, inactiveNyRule, Rule(Guid.NewGuid(), Chicago));
        var nySeries = Weekly(NewYork);
        var cancelledNySeries = Weekly(NewYork);
        Assert.True(cancelledNySeries.Cancel(cancelledNySeries.OrganizerUserId, true, null, Now).IsSuccess);
        db.Appointments.AddRange(nySeries, cancelledNySeries, Weekly(Denver), OneOff(NewYork));
        // En SQL el rowversion lo pone la base; en memoria hay que darle uno.
        foreach (var entry in db.ChangeTracker.Entries<Appointment>())
            entry.Property(x => x.RowVersion).CurrentValue = new byte[8];
        await db.SaveChangesAsync();
        var changes = new OfficeTimeZoneChangeRepository(db);
        var evt = Changed(NewYork, Chicago, 2);

        await OfficeTimeZoneChangeConsumer.Handle(
            evt,
            changes,
            new OfficeTimeZoneCandidates(db),
            db,
            CancellationToken.None
        );
        // Reentrega del mismo evento: sin un segundo registro.
        await OfficeTimeZoneChangeConsumer.Handle(
            evt,
            changes,
            new OfficeTimeZoneCandidates(db),
            db,
            CancellationToken.None
        );

        Assert.Equal(1, await db.OfficeTimeZoneChanges.IgnoreQueryFilters().CountAsync());
        var preview = (
            await GetOfficeTimeZoneChangePreviewHandler.Handle(
                new GetOfficeTimeZoneChangePreviewQuery(Tenant, Now),
                changes,
                new OfficeTimeZoneCandidates(db),
                CancellationToken.None
            )
        ).Value!;
        Assert.Equal(NewYork, preview.FromTimeZoneId);
        Assert.Equal(Chicago, preview.ToTimeZoneId);
        Assert.Equal([nyRule.Id], preview.Rules.Select(r => r.RuleId));
        Assert.Equal(preparer, preview.Rules.Single().UserId);
        Assert.Equal([nySeries.Id], preview.Series.Select(s => s.AppointmentId));
    }

    // En vivo: sin horarios ni series en la zona anterior no hay nada que decidir; un pendiente así quedaría
    // invisible y fijaría una zona de origen equivocada para el cambio siguiente.
    [Fact]
    public async Task A_change_with_nothing_to_move_is_not_recorded()
    {
        await using var db = NewDb();
        db.AvailabilityRules.Add(Rule(Guid.NewGuid(), Denver));
        await db.SaveChangesAsync();
        var changes = new OfficeTimeZoneChangeRepository(db);

        await OfficeTimeZoneChangeConsumer.Handle(
            Changed(NewYork, Chicago, 2),
            changes,
            new OfficeTimeZoneCandidates(db),
            db,
            CancellationToken.None
        );

        Assert.Equal(0, await db.OfficeTimeZoneChanges.IgnoreQueryFilters().CountAsync());
    }

    private static TenantTimeZoneChangedIntegrationEvent Changed(string from, string to, long version) =>
        new()
        {
            TenantId = Tenant,
            ChangedTenantId = Tenant,
            PreviousTimeZoneId = from,
            NewTimeZoneId = to,
            ChangedAtUtc = Now,
            TenantVersion = version,
            CorrelationId = "c",
        };

    private static AvailabilityRule Rule(Guid userId, string zone) =>
        AvailabilityRule
            .Create(Tenant, userId, DaysOfWeekMask.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0), zone, Now)
            .Value;

    private static Appointment Weekly(string zone)
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 11, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), zone)
            .Value;
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

    private static Appointment OneOff(string zone) =>
        Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Client meeting").Value,
                EventTiming.PointInTimeOf(Now.AddDays(3), Now.AddDays(3).AddHours(1), zone).Value,
                Guid.NewGuid(),
                Guid.NewGuid(),
                Now
            )
            .Value;

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
