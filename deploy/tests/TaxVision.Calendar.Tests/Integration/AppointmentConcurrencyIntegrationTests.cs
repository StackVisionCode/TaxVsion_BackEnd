using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// BB-12: <c>RowVersion</c> de la cita llega como <c>ConflictException</c> (409 por HTTP, reintento por mensaje), y un
/// cambio en asistentes o excepciones toca la raíz. Sin eso, el conflicto no se ve.
/// </summary>
public sealed class AppointmentConcurrencyIntegrationTests : IAsyncLifetime
{
    private const string ConcurrencyConflict = "Persistence.ConcurrencyConflict";
    private static readonly DateTime Now = DateTime.UtcNow;

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task TwoEdits_FromSameRead_SecondGetsConflict()
    {
        var id = await SeedOneOffAsync(Now.AddDays(2));

        await AssertSecondConflictsAsync(
            id,
            a => a.ChangeTitle(AppointmentTitle.Create("First").Value, _organizer, false),
            a => a.ChangeTitle(AppointmentTitle.Create("Second").Value, _organizer, false)
        );

        await using var read = _database.NewContext();
        var stored = await read.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        Assert.Equal("First", stored.Title.Value);
    }

    [Fact]
    public async Task TwoAttendeeAdds_FromSameRead_SecondGetsConflict()
    {
        var id = await SeedOneOffAsync(Now.AddDays(2));

        await AssertSecondConflictsAsync(id, a => Invite(a, "first@firm.test"), a => Invite(a, "second@firm.test"));

        await using var read = _database.NewContext();
        var stored = await read
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .SingleAsync(a => a.Id == id);
        Assert.Single(stored.Attendees);
    }

    [Fact]
    public async Task AttendeeRemovedWhileAnotherIsAdded_SecondGetsConflict()
    {
        var id = await SeedOneOffAsync(Now.AddDays(2), "stays@firm.test");
        Guid attendeeId;
        await using (var read = _database.NewContext())
        {
            attendeeId = (
                await read.Appointments.IgnoreQueryFilters().Include(a => a.Attendees).SingleAsync(a => a.Id == id)
            )
                .Attendees[0]
                .Id;
        }

        await AssertSecondConflictsAsync(
            id,
            a => a.RemoveAttendee(attendeeId, _organizer, false),
            a => Invite(a, "second@firm.test")
        );
    }

    [Fact]
    public async Task ResponseWhileRescheduling_SecondGetsConflict()
    {
        var id = await SeedOneOffAsync(Now.AddDays(2), "guest@firm.test");

        await AssertSecondConflictsAsync(
            id,
            a =>
                a.Reschedule(
                    EventTiming.PointInTimeOf(Now.AddDays(3), Now.AddDays(3).AddHours(1), "America/New_York").Value,
                    _organizer,
                    false,
                    Now
                ),
            a => a.RespondAsAttendee(AttendeeResponse.Accepted, null, null, "guest@firm.test", Now)
        );
    }

    [Fact]
    public async Task OccurrenceExceptions_FromSameRead_SecondGetsConflict()
    {
        var id = await SeedSeriesAsync();
        await using var read = _database.NewContext();
        var series = await read.Appointments.IgnoreQueryFilters().SingleAsync(a => a.Id == id);
        var starts = OccurrenceExpander.Expand(series, Now, Now.AddDays(30)).Value;

        await AssertSecondConflictsAsync(
            id,
            a => a.CancelOccurrence(starts[0].OriginalStartUtc, _organizer, false, Now),
            a => a.OverrideOccurrence(starts[1].OriginalStartUtc, null, null, "Moved", null, _organizer, false, Now)
        );
    }

    /// <summary>Tras BB-12 el conflicto ya no es un <c>DbUpdateException</c>: el lote perdido sigue sin frenar a los demás.</summary>
    [Fact]
    public async Task RetentionJob_AChunkInConflict_DoesNotStopTheNextOne()
    {
        var old = Now.AddYears(-8);
        for (var i = 0; i < 51; i++)
            await SeedOneOffAsync(old.AddMinutes(i));

        var failOnce = new FailOnceWithConcurrency();
        var services = new ServiceCollection();
        services.AddDbContext<CalendarDbContext>(db =>
            db.UseSqlServer(_database.ConnectionString).AddInterceptors(failOnce)
        );
        services.AddSingleton<ITenantContext, CalendarTemporaryDatabase.NoTenantContext>();
        await using var provider = services.BuildServiceProvider();

        await new CalendarRetentionJob(
            provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new StartingSoonOptions()),
            NullLogger<CalendarRetentionJob>.Instance
        ).PurgeAsync(CancellationToken.None);

        await using var read = _database.NewContext();
        Assert.Equal(50, await read.Appointments.IgnoreQueryFilters().CountAsync());
    }

    private async Task AssertSecondConflictsAsync(
        Guid id,
        Func<Appointment, Result> first,
        Func<Appointment, Result> second
    )
    {
        await using var winnerDb = _database.NewContext();
        await using var loserDb = _database.NewContext();
        var winner = await LoadAsync(winnerDb, id);
        var loser = await LoadAsync(loserDb, id);

        Assert.True(first(winner).IsSuccess);
        Assert.True(second(loser).IsSuccess);

        await winnerDb.SaveChangesAsync();
        var conflict = await Assert.ThrowsAsync<ConflictException>(() => loserDb.SaveChangesAsync());
        Assert.Equal(ConcurrencyConflict, conflict.Code);
    }

    private static Task<Appointment> LoadAsync(CalendarDbContext db, Guid id) =>
        db
            .Appointments.IgnoreQueryFilters()
            .Include(a => a.Attendees)
            .Include(a => a.Exceptions)
            .SingleAsync(a => a.Id == id);

    private Result Invite(Appointment appointment, string email) =>
        appointment.AddAttendee(
            AttendeeKind.External,
            null,
            null,
            AttendeeSnapshot.Create("Guest", email).Value,
            false,
            _organizer,
            Now,
            false
        );

    private async Task<Guid> SeedOneOffAsync(DateTime startUtc, string? attendeeEmail = null)
    {
        var appointment = NewAppointment(
            EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), "America/New_York").Value
        );
        if (attendeeEmail is not null)
            Invite(appointment, attendeeEmail);
        return await SaveAsync(appointment);
    }

    private async Task<Guid> SeedSeriesAsync()
    {
        var appointment = NewAppointment(
            EventTiming.PointInTimeOf(Now.AddDays(1), Now.AddDays(1).AddHours(1), "America/New_York").Value
        );
        var series = EventTiming
            .RecurringOf(DateOnly.FromDateTime(Now), new TimeOnly(9, 0), TimeSpan.FromHours(1), "America/New_York")
            .Value;
        appointment.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY").Value, series, _organizer, false);
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

    private sealed class FailOnceWithConcurrency : SaveChangesInterceptor
    {
        private int _calls;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            Interlocked.Increment(ref _calls) == 1
                ? throw new DbUpdateConcurrencyException("Rival.")
                : ValueTask.FromResult(result);
    }
}
