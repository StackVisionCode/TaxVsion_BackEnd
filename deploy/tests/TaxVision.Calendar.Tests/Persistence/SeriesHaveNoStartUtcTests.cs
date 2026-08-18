using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Calendar.Tests.Persistence;

/// <summary>
/// La invariante de ADR-C-03, comprobada sobre la tabla y no sobre el código: una serie no tiene
/// <c>StartUtc</c>.
///
/// <para>
/// Se prueba contra datos porque el error no entra por el agregado: entra el día que alguien
/// «arregla» el NULL con un UPDATE o con un valor por defecto en la migración, viéndolo como un
/// campo olvidado. Guardar el primer inicio en UTC congela el offset de ese día y corre la serie
/// entera una hora al cambiar el horario de verano.
/// </para>
/// </summary>
[Collection(nameof(SqlServerCollection))]
public sealed class SeriesHaveNoStartUtcTests : IAsyncLifetime
{
    private readonly Guid _tenant = Guid.NewGuid();
    private CalendarDbContext _context = default!;

    public Task InitializeAsync()
    {
        _context = SqlServerFixture.CreateContext(_tenant);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        await _context.Appointments.IgnoreQueryFilters().Where(a => a.TenantId == _tenant).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }

    [Fact]
    public async Task No_stored_series_has_a_start_instant()
    {
        await SeedAsync();

        var offenders = await _context
            .Appointments.IgnoreQueryFilters()
            .Where(a => a.Recurrence != null && a.Timing.StartUtc != null)
            .Select(a => a.Id)
            .ToListAsync();

        Assert.True(offenders.Count == 0, "Series with a StartUtc: " + string.Join(", ", offenders));
    }

    /// <summary>La contraparte: una puntual sin instante sería una cita que no ocurre nunca.</summary>
    [Fact]
    public async Task No_stored_one_off_is_missing_its_start_instant()
    {
        await SeedAsync();

        var offenders = await _context
            .Appointments.IgnoreQueryFilters()
            .Where(a => a.Recurrence == null && a.Timing.StartUtc == null && a.Timing.StartDate == null)
            .Select(a => a.Id)
            .ToListAsync();

        Assert.True(offenders.Count == 0, "One-off appointments without a start: " + string.Join(", ", offenders));
    }

    private async Task SeedAsync()
    {
        var organizer = Guid.NewGuid();
        var nowUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var recurring = EventTiming
            .RecurringOf(new DateOnly(2026, 3, 2), new TimeOnly(9, 0), TimeSpan.FromHours(1), "America/New_York")
            .Value;

        var series = Appointment
            .Schedule(_tenant, AppointmentTitle.Create("Serie").Value, recurring, Guid.NewGuid(), organizer, nowUtc)
            .Value;
        series.MakeRecurring(RecurrenceRule.Create("FREQ=WEEKLY;BYDAY=MO").Value, recurring, organizer);

        var pointInTime = EventTiming
            .PointInTimeOf(
                new DateTime(2026, 3, 3, 14, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 3, 3, 15, 0, 0, DateTimeKind.Utc),
                "America/New_York"
            )
            .Value;

        var single = Appointment
            .Schedule(_tenant, AppointmentTitle.Create("Puntual").Value, pointInTime, Guid.NewGuid(), organizer, nowUtc)
            .Value;

        _context.Appointments.AddRange(series, single);
        await _context.SaveChangesAsync();
    }
}
