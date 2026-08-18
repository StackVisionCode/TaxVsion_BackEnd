using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Jobs;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Persistence;

/// <summary>
/// Se prueba <b>la consulta del job</b>, no una copia suya: una copia documenta el comportamiento pero
/// no impide que alguien le quite el `Include` al job.
///
/// <para>
/// Los jobs leen `Attendees` y `Exceptions` de las citas que cargan. Sin el `Include`, EF devuelve las
/// dos colecciones **vacías** y nada falla: `StartingSoonJob` decide que no hay a quién avisar, y el
/// expander de `ReminderScheduleJob` no ve ni una cancelación, así que pide avisos de reuniones que ya
/// no existen.
///
/// <para>
/// Va contra SQL Server real porque es exactamente lo que un doble en memoria no reproduce: ahí las
/// colecciones están pobladas siempre.
/// </para>
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class JobQueriesLoadWhatTheyReadTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _organizer = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private CalendarDbContext _context = default!;

    public async Task InitializeAsync()
    {
        _context = SqlServerFixture.CreateContext(_tenant);
        await SeedAsync();
    }

    public async Task DisposeAsync()
    {
        await _context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }

    /// <summary>La consulta de <c>StartingSoonJob</c>, tal cual.</summary>
    [Fact]
    public async Task The_starting_soon_query_loads_the_attendees_it_reads()
    {
        await using var fresh = SqlServerFixture.CreateContext(_tenant);

        var loaded = await StartingSoonJob.Candidates(fresh).Where(a => a.TenantId == _tenant).ToListAsync();

        Assert.NotEmpty(loaded);
        Assert.Contains(loaded, a => a.Attendees.Count > 0);
    }

    /// <summary>La de <c>ReminderScheduleJob</c>: sin las excepciones, el expander no ve la cancelada.</summary>
    [Fact]
    public async Task The_reminder_query_loads_the_exceptions_the_expander_reads()
    {
        await using var fresh = SqlServerFixture.CreateContext(_tenant);

        var loaded = await ReminderScheduleJob.Candidates(fresh).Where(a => a.TenantId == _tenant).ToListAsync();

        Assert.NotEmpty(loaded);
        Assert.Contains(loaded, a => a.Exceptions.Count > 0);
    }

    /// <summary>
    /// La contraparte que explica por qué los dos tests de arriba valen: la misma consulta sin
    /// `Include` devuelve las colecciones vacías, y ningún error.
    /// </summary>
    [Fact]
    public async Task Without_the_include_both_collections_come_back_empty()
    {
        await using var fresh = SqlServerFixture.CreateContext(_tenant);

        var loaded = await fresh.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ToListAsync();

        Assert.NotEmpty(loaded);
        Assert.All(loaded, a => Assert.Empty(a.Attendees));
        Assert.All(loaded, a => Assert.Empty(a.Exceptions));
    }

    private async Task SeedAsync()
    {
        var timing = EventTiming
            .RecurringOf(new DateOnly(2026, 3, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), "America/New_York")
            .Value;

        var series = Appointment
            .Schedule(_tenant, AppointmentTitle.Create("Serie").Value, timing, Guid.NewGuid(), _organizer, Now)
            .Value;

        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, timing, _organizer);
        series.RequestReminder(15);
        series.AddAttendee(
            AttendeeKind.InternalUser,
            Guid.NewGuid(),
            null,
            AttendeeSnapshot.Create("Empleado", "empleado@example.com").Value,
            isRequired: true,
            _organizer,
            Now
        );
        // 9 de marzo de 2026 es lunes y ya esta en horario de verano: 9:00 en Nueva York son las
        // 13:00Z, no las 14:00Z. Se comprueba el Result porque una hora que no es ocurrencia se
        // rechaza en silencio y el test pasaria sin excepcion que cargar.
        Assert.True(
            series.CancelOccurrence(new DateTime(2026, 3, 9, 13, 0, 0, DateTimeKind.Utc), _organizer, Now).IsSuccess
        );

        _context.Appointments.Add(series);
        await _context.SaveChangesAsync();
    }
}
