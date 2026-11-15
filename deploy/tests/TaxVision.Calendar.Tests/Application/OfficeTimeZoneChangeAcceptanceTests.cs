using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Application.Availability.Queries;
using TaxVision.Calendar.Application.Office.TimeZoneChanges;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Availability;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Application;

/// <summary>
/// Aceptación de la Fase 8 (P9): la oficina pasa de Nueva York a Chicago y el admin confirma mover el horario del
/// preparador. La disponibilidad pública queda de 9:00 a 17:00 de Chicago y la cita ya acordada no se mueve.
/// </summary>
public sealed class OfficeTimeZoneChangeAcceptanceTests
{
    private const string NewYork = "America/New_York";
    private const string Chicago = "America/Chicago";
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Preparer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 11, 14, 15, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Moving_new_york_hours_to_chicago_keeps_booked_appointments_where_they_are()
    {
        await using var db = NewDb();
        var rule = AvailabilityRule
            .Create(Tenant, Preparer, DaysOfWeekMask.Weekdays, new TimeOnly(9, 0), new TimeOnly(17, 0), NewYork, Now)
            .Value;
        // Acordada para el lunes 16-nov a las 10:00 de Nueva York (15:00Z).
        var booked = Appointment
            .Schedule(
                Tenant,
                AppointmentTitle.Create("Client meeting").Value,
                EventTiming
                    .PointInTimeOf(
                        new DateTime(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc),
                        new DateTime(2026, 11, 16, 16, 0, 0, DateTimeKind.Utc),
                        NewYork
                    )
                    .Value,
                Guid.NewGuid(),
                Preparer,
                Now
            )
            .Value;
        db.AvailabilityRules.Add(rule);
        db.Appointments.Add(booked);
        foreach (var entry in db.ChangeTracker.Entries<Appointment>())
            entry.Property(x => x.RowVersion).CurrentValue = new byte[8];
        await db.SaveChangesAsync();

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
        var candidates = new OfficeTimeZoneCandidates(db);
        var preview = (
            await GetOfficeTimeZoneChangePreviewHandler.Handle(
                new GetOfficeTimeZoneChangePreviewQuery(Tenant, Now),
                changes,
                candidates,
                CancellationToken.None
            )
        ).Value!;
        Assert.Equal([rule.Id], preview.Rules.Select(r => r.RuleId));

        var applied = await ApplyOfficeTimeZoneChangeHandler.Handle(
            new ApplyOfficeTimeZoneChangeCommand(Tenant, Guid.NewGuid(), preview.ChangeId, [rule.Id], [], Now),
            changes,
            candidates,
            db,
            new FakeMessageBus(),
            CancellationToken.None
        );
        Assert.True(applied.IsSuccess);

        var free = (
            await GetAvailabilityHandler.Handle(
                new GetAvailabilityQuery(
                    Tenant,
                    Preparer,
                    new DateTime(2026, 11, 16, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 17, 0, 0, 0, DateTimeKind.Utc),
                    null
                ),
                new AppointmentRepository(db),
                new AvailabilityRepository(db),
                new AppointmentTypeRepository(db),
                new OfficeScheduleRepository(db),
                CancellationToken.None
            )
        ).Value;

        // 9:00–17:00 de Chicago = 15:00Z–23:00Z; la cita acordada sigue a las 15:00Z y ocupa su hora.
        Assert.Equal(
            [
                (
                    new DateTime(2026, 11, 16, 16, 0, 0, DateTimeKind.Utc),
                    new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc)
                ),
            ],
            free.Select(s => (s.StartUtc, s.EndUtc))
        );
        Assert.Equal(new DateTime(2026, 11, 16, 15, 0, 0, DateTimeKind.Utc), booked.Timing.StartUtc);
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
