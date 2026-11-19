using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Calendar.Application.Appointments.Abstractions;
using TaxVision.Calendar.Application.Appointments.Work;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// Work Profile contra SQL Server: los conteos usan la visibilidad por asignación del que mira y nunca leen otra
/// oficina, aunque el mismo usuario tenga citas allí.
/// </summary>
public sealed class AppointmentWorkIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();
    private readonly Guid _clientA = Guid.NewGuid();
    private readonly Guid _clientB = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task Counts_only_what_the_viewer_sees_and_never_another_office()
    {
        var assigned = await SeedAsync(_tenant, Now.AddDays(1), _clientA);
        var hidden = await SeedAsync(_tenant, Now.AddDays(2), _clientB);
        var internalOne = await SeedAsync(_tenant, Now.AddDays(3), null);
        await SeedAsync(_otherTenant, Now.AddDays(1), null);
        await using (var db = _database.NewContext())
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenant, _clientA, _andres, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        var restricted = await SummaryAsync(canViewAll: false);
        var full = await SummaryAsync(canViewAll: true);
        var batch = await BatchAsync(canViewAll: false);

        Assert.Equal(2, restricted.Counts[0].Open);
        Assert.Equal([assigned, internalOne], restricted.Top.Select(i => i.Id));
        Assert.Equal(3, full.Counts[0].Open);
        Assert.Equal([assigned, hidden, internalOne], full.Top.Select(i => i.Id));
        Assert.Equal(2, Assert.Single(batch.Rows).Open);
    }

    private async Task<BuildingBlocks.Contracts.Work.WorkSummaryResponse> SummaryAsync(bool canViewAll)
    {
        await using var db = _database.NewContext();
        var result = await GetAppointmentWorkHandler.Handle(
            new GetAppointmentWorkQuery(
                _tenant,
                _olga,
                _andres,
                canViewAll,
                ResolvedTimeZone.Resolve("America/New_York"),
                Now
            ),
            new AppointmentRepository(db),
            VisibilityOn(),
            default
        );
        return result.Value;
    }

    private async Task<BuildingBlocks.Contracts.Work.WorkBatchResponse> BatchAsync(bool canViewAll)
    {
        await using var db = _database.NewContext();
        var result = await GetAppointmentWorkBatchHandler.Handle(
            new GetAppointmentWorkBatchQuery(
                _tenant,
                [_olga],
                _andres,
                canViewAll,
                ResolvedTimeZone.Resolve("America/New_York"),
                Now
            ),
            new AppointmentRepository(db),
            VisibilityOn(),
            default
        );
        return result.Value;
    }

    private static IOptions<CalendarVisibilityOptions> VisibilityOn() =>
        Options.Create(new CalendarVisibilityOptions { Enabled = true });

    private async Task<Guid> SeedAsync(Guid tenant, DateTime startUtc, Guid? customer)
    {
        var appointment = Appointment
            .Schedule(
                tenant,
                AppointmentTitle.Create("Review").Value,
                EventTiming.PointInTimeOf(startUtc, startUtc.AddHours(1), "America/New_York").Value,
                Guid.NewGuid(),
                _olga,
                Now.AddDays(-1),
                customerId: customer
            )
            .Value;

        await using var db = _database.NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }
}
