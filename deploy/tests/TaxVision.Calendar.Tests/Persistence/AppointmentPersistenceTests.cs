using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Scheduling;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Persistence;

/// <summary>
/// Contra SQL Server real, no InMemory: InMemory ignora los índices únicos y el <c>rowversion</c>, y
/// sobre todo <b>no reproduce la lectura de <c>datetime2</c></b> — que es donde vive el bug que este
/// servicio no se puede permitir.
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class AppointmentPersistenceTests : IAsyncLifetime
{
    private const string NewYork = "America/New_York";

    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private CalendarDbContext _context = default!;

    public Task InitializeAsync()
    {
        _context = SqlServerFixture.CreateContext(_tenant);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        // Cada test se lleva sus filas: la base es compartida y un residuo degrada al siguiente.
        await _context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }

    private Appointment WeeklySeries()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 1, 5), new TimeOnly(9, 0), TimeSpan.FromHours(1), NewYork)
            .Value;

        var series = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create("Revision semanal").Value,
                timing,
                Guid.NewGuid(),
                _organizer,
                Now
            )
            .Value;

        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, _organizer);
        return series;
    }

    [Fact]
    public async Task A_series_survives_the_round_trip_and_still_expands_to_the_same_local_hours()
    {
        var series = WeeklySeries();
        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();

        var before = OccurrenceExpander
            .Expand(
                series,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            )
            .Value;

        // Contexto nuevo: nada cacheado, se relee de verdad.
        await using var fresh = SqlServerFixture.CreateContext(_tenant);
        var reloaded = await fresh.Appointments.FirstAsync(a => a.Id == series.Id);

        var after = OccurrenceExpander
            .Expand(
                reloaded,
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc)
            )
            .Value;

        Assert.Equal(before.Count, after.Count);
        for (var i = 0; i < before.Count; i++)
            Assert.Equal(before[i].StartUtc, after[i].StartUtc);

        // Y el DST sigue aplicado: enero a las 14:00Z, julio a las 13:00Z, las dos son las 9:00 locales.
        Assert.Contains(after, o => o.StartUtc == new DateTime(2026, 1, 12, 14, 0, 0, DateTimeKind.Utc));
        Assert.Contains(after, o => o.StartUtc == new DateTime(2026, 7, 13, 13, 0, 0, DateTimeKind.Utc));
    }

    [Fact]
    public async Task Dates_come_back_from_the_database_marked_as_utc()
    {
        // L-1: SQL Server devuelve datetime2 con Kind Unspecified. Sin los convertidores del
        // DbContext, una cita válida se rechaza a sí misma al releerla y la única pista es una Z que
        // falta en la respuesta.
        var timing = EventTiming
            .PointInTimeOf(
                new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 10, 15, 0, 0, DateTimeKind.Utc),
                NewYork
            )
            .Value;

        var appointment = Appointment
            .Schedule(_tenant, AppointmentTitle.Create("Puntual").Value, timing, Guid.NewGuid(), _organizer, Now)
            .Value;

        _context.Appointments.Add(appointment);
        await _context.SaveChangesAsync();

        await using var fresh = SqlServerFixture.CreateContext(_tenant);
        var reloaded = await fresh.Appointments.FirstAsync(a => a.Id == appointment.Id);

        Assert.Equal(DateTimeKind.Utc, reloaded.Timing.StartUtc!.Value.Kind);
        Assert.Equal(DateTimeKind.Utc, reloaded.CreatedAtUtc.Kind);

        // El invariante que exige UTC acepta el dato releído: es la prueba de que no se rechaza a sí mismo.
        var rebuilt = EventTiming.PointInTimeOf(
            reloaded.Timing.StartUtc!.Value,
            reloaded.Timing.EndUtc!.Value,
            NewYork
        );
        Assert.True(rebuilt.IsSuccess);
    }

    [Fact]
    public async Task A_recurring_series_is_stored_with_a_null_start_utc()
    {
        var series = WeeklySeries();
        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();

        await using var fresh = SqlServerFixture.CreateContext(_tenant);
        var reloaded = await fresh.Appointments.FirstAsync(a => a.Id == series.Id);

        // Parece un bug y es ADR-C-03. Rellenar esta columna reintroduce el bug de DST entero.
        Assert.Null(reloaded.Timing.StartUtc);
        Assert.Equal(new TimeOnly(9, 0), reloaded.Timing.LocalStartTime);
        Assert.Equal(NewYork, reloaded.Timing.TimeZone.Id);
    }

    [Fact]
    public async Task The_split_gives_each_series_its_own_timing_row()
    {
        // L-2: EF no admite la misma instancia de un owned type en dos propietarios — la persiste en
        // uno y deja el otro en NULL. El test que compara valores pasa igual; hay que comparar
        // identidad de instancia y, sobre todo, releer.
        var original = WeeklySeries();
        _context.Appointments.Add(original);
        await _context.SaveChangesAsync();

        // Se parte pasando LA MISMA instancia de timing: es lo que hace un «esta y las siguientes»
        // que solo cambia la regla —de semanal a quincenal— y deja la hora igual. Con la instancia
        // compartida, EF persiste el timing en una serie y deja la otra en NULL.
        var follower = original
            .SplitForFollowing(
                new DateTime(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc),
                original.Timing,
                RecurrenceRule.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO").Value,
                _organizer,
                Now
            )
            .Value;

        _context.Appointments.Add(follower);
        await _context.SaveChangesAsync();

        Assert.NotSame(original.Timing, follower.Timing);

        await using var fresh = SqlServerFixture.CreateContext(_tenant);
        var reloadedOriginal = await fresh.Appointments.FirstAsync(a => a.Id == original.Id);
        var reloadedFollower = await fresh.Appointments.FirstAsync(a => a.Id == follower.Id);

        // Las dos tienen su fila de timing: ninguna quedo en blanco.
        Assert.Equal(new TimeOnly(9, 0), reloadedOriginal.Timing.LocalStartTime);
        Assert.Equal(new TimeOnly(9, 0), reloadedFollower.Timing.LocalStartTime);
        Assert.Equal(NewYork, reloadedFollower.Timing.TimeZone.Id);
    }

    [Fact]
    public async Task Attendees_copied_to_the_new_series_keep_their_own_snapshot_row()
    {
        var original = WeeklySeries();
        original.AddAttendee(
            AttendeeKind.InternalUser,
            Guid.NewGuid(),
            null,
            AttendeeSnapshot.Create("Ana Preparadora", "ana@firma.test").Value,
            isRequired: true,
            _organizer,
            Now
        );
        _context.Appointments.Add(original);
        await _context.SaveChangesAsync();

        var follower = original
            .SplitForFollowing(
                new DateTime(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc),
                EventTiming
                    .RecurringOf(new DateOnly(2026, 3, 9), new TimeOnly(10, 0), TimeSpan.FromHours(1), NewYork)
                    .Value,
                RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value,
                _organizer,
                Now
            )
            .Value;

        _context.Appointments.Add(follower);
        await _context.SaveChangesAsync();

        Assert.NotSame(original.Attendees[0].Snapshot, follower.Attendees[0].Snapshot);

        await using var fresh = SqlServerFixture.CreateContext(_tenant);
        var reloaded = await fresh.Appointments.Include(a => a.Attendees).FirstAsync(a => a.Id == follower.Id);

        // Compartir la instancia dejaría este DisplayName en NULL y el de la otra serie intacto.
        Assert.Equal("Ana Preparadora", reloaded.Attendees[0].Snapshot.DisplayName);
    }

    [Fact]
    public async Task Two_exceptions_for_the_same_occurrence_are_refused_by_the_database()
    {
        // El agregado ya lo comprueba, pero la segunda peticion concurrente leyo la serie ANTES de que
        // la primera guardara: su lista de excepciones esta vacia, pasa la comprobacion y llega a la
        // base. Solo el indice unico las separa — e InMemory no lo aplicaria.
        var series = WeeklySeries();
        var target = new DateTime(2026, 1, 19, 14, 0, 0, DateTimeKind.Utc);
        series.CancelOccurrence(target, _organizer, Now);

        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();

        await using var stale = SqlServerFixture.CreateContext(_tenant);
        var readBeforeTheOtherSaved = await stale.Appointments.FirstAsync(a => a.Id == series.Id);

        Assert.True(readBeforeTheOtherSaved.CancelOccurrence(target, _organizer, Now).IsSuccess);

        await Assert.ThrowsAnyAsync<Exception>(() => stale.SaveChangesAsync());
    }

    [Fact]
    public async Task A_tenant_cannot_read_another_tenants_appointment()
    {
        var series = WeeklySeries();
        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();

        var stranger = Guid.NewGuid();
        await using var otherTenant = SqlServerFixture.CreateContext(stranger);
        var repository = new AppointmentRepository(otherTenant);

        var byFilter = await otherTenant.Appointments.FirstOrDefaultAsync(a => a.Id == series.Id);
        var byRepository = await repository.GetByIdAsync(stranger, series.Id);

        Assert.Null(byFilter);
        Assert.True(byRepository.IsFailure);
    }

    [Fact]
    public async Task A_job_without_a_tenant_in_context_sees_nothing_unless_it_asks_explicitly()
    {
        var series = WeeklySeries();
        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();

        // Es el escenario del scope de Wolverine: sin tenant, el filtro fail-closed compara contra
        // Guid.Empty. Un job que no pida IgnoreQueryFilters() ve 0 filas y parece sano.
        await using var noTenant = SqlServerFixture.CreateContext(Guid.Empty);

        var filtered = await noTenant.Appointments.CountAsync(a => a.Id == series.Id);
        var explicitly = await noTenant
            .Appointments.IgnoreQueryFilters()
            .CountAsync(a => a.Id == series.Id && a.TenantId == _tenant);

        Assert.Equal(0, filtered);
        Assert.Equal(1, explicitly);
    }
}
