using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>CAL-3: la ventana se filtra en SQL, no leyendo la tabla entera.</summary>
public sealed class AppointmentRepositoryIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task ListStartingWithin_ExcludesPastOneOffs()
    {
        var past = await SeedOneOffAsync(Now.AddDays(-3));
        var inWindow = await SeedOneOffAsync(Now.AddMinutes(10));
        var later = await SeedOneOffAsync(Now.AddDays(3));
        var ended = await SeedSeriesAsync("FREQ=WEEKLY;UNTIL=20260101T000000Z");
        var endingAhead = await SeedSeriesAsync("FREQ=WEEKLY;UNTIL=20270101T000000Z");
        var endless = await SeedSeriesAsync("FREQ=WEEKLY");

        var ids = await ListAsync();

        Assert.Contains(inWindow, ids);
        Assert.Contains(endingAhead, ids);
        Assert.Contains(endless, ids);
        Assert.DoesNotContain(past, ids);
        Assert.DoesNotContain(later, ids);
        Assert.DoesNotContain(ended, ids);
    }

    [Fact]
    public async Task ListStartingWithin_ExcludesCancelled()
    {
        var cancelled = await SeedOneOffAsync(Now.AddMinutes(10), cancel: true);

        Assert.DoesNotContain(cancelled, await ListAsync());
    }

    private async Task<List<Guid>> ListAsync()
    {
        await using var db = _database.NewContext();
        return await AppointmentRepository
            .ListStartingWithin(db, Now, Now.AddMinutes(20))
            .Select(a => a.Id)
            .ToListAsync();
    }

    private async Task<Guid> SeedOneOffAsync(DateTime startUtc, bool cancel = false)
    {
        var timing = EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), "America/New_York").Value;
        var appointment = NewAppointment(timing);
        if (cancel)
            appointment.Cancel(_organizer, false, null, Now);
        return await SaveAsync(appointment);
    }

    private async Task<Guid> SeedSeriesAsync(string rule)
    {
        var appointment = NewAppointment(
            EventTiming.PointInTimeOf(Now.AddYears(-1), Now.AddYears(-1).AddHours(1), "America/New_York").Value
        );
        var series = EventTiming
            .RecurringOf(new DateOnly(2025, 11, 3), new TimeOnly(9, 0), TimeSpan.FromHours(1), "America/New_York")
            .Value;
        appointment.MakeRecurring(RecurrenceRule.Create(rule).Value, series, _organizer, false);
        return await SaveAsync(appointment);
    }

    private Appointment NewAppointment(EventTiming timing) =>
        Appointment
            .Schedule(
                _tenantId,
                AppointmentTitle.Create("Client review").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                Now
            )
            .Value;

    private async Task<Guid> SaveAsync(Appointment appointment)
    {
        await using var db = _database.NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }
}
