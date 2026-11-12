using BuildingBlocks.CustomerVisibility;
using TaxVision.Calendar.Application.Appointments.Search;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// La búsqueda contra SQL Server: texto en título, lugar, notas y asistentes; los comodines del usuario
/// son literales; respeta la visibilidad por asignación; el portal solo ve lo suyo y solo por título y lugar.
/// </summary>
public sealed class AppointmentSearchIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(10).AddHours(15);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();
    private readonly Guid _andres = Guid.NewGuid();
    private readonly Guid _clientA = Guid.NewGuid();
    private readonly Guid _clientB = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    private AppointmentSearchCriteria Criteria(string? text, Guid? assignedTo = null, Guid? portalCustomer = null) =>
        new(_tenant, text, Start.AddDays(-30), Start.AddDays(30), assignedTo, portalCustomer, null, null, null, 50);

    [Fact]
    public async Task Text_matches_title_location_notes_and_people_and_wildcards_are_literal()
    {
        var byTitle = await SeedAsync("Revision 1040", null, null, null);
        var byLocation = await SeedAsync("Consulta", "Oficina Santiago", null, null);
        var byPerson = await SeedAsync("Consulta", null, null, "Minato Kenyimura");
        var percent = await SeedAsync("Descuento 100% firma", null, null, null);

        Assert.Equal([byTitle], await IdsAsync(Criteria("1040")));
        Assert.Equal([byLocation], await IdsAsync(Criteria("santiago")));
        Assert.Equal([byPerson], await IdsAsync(Criteria("kenyimura")));
        // "%" y "_" del usuario son caracteres, no comodines.
        Assert.Equal([percent], await IdsAsync(Criteria("100%")));
        Assert.Empty(await IdsAsync(Criteria("_")));
    }

    [Fact]
    public async Task Without_view_all_only_the_assigned_clients_and_the_portal_only_its_own_by_title_or_place()
    {
        var mineClient = await SeedAsync("Revision A", null, _clientA, "Ana Guest");
        var otherClient = await SeedAsync("Revision B", null, _clientB, null);
        await using (var db = _database.NewContext())
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenant, _clientA, _andres, DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        Assert.Equal([mineClient], await IdsAsync(Criteria("revision", assignedTo: _andres)));
        Assert.Equal([mineClient], await IdsAsync(Criteria("revision", portalCustomer: _clientA)));
        // El portal no busca por otros invitados: no puede confirmar quién más va.
        Assert.Empty(await IdsAsync(Criteria("ana guest", portalCustomer: _clientA)));
        Assert.DoesNotContain(otherClient, await IdsAsync(Criteria("revision", portalCustomer: _clientA)));
    }

    private async Task<List<Guid>> IdsAsync(AppointmentSearchCriteria criteria)
    {
        await using var db = _database.NewContext();
        return [.. (await new AppointmentSearch(db).SearchAsync(criteria)).Select(a => a.Id).Order()];
    }

    private async Task<Guid> SeedAsync(string title, string? location, Guid? customer, string? attendee)
    {
        var appointment = Appointment
            .Schedule(
                _tenant,
                AppointmentTitle.Create(title).Value,
                EventTiming.PointInTimeOf(Start, Start.AddMinutes(30), "America/New_York").Value,
                Guid.NewGuid(),
                _olga,
                DateTime.UtcNow,
                location: location is null ? null : Location.Create(location).Value,
                customerId: customer
            )
            .Value;
        if (attendee is not null)
            appointment.AddAttendee(
                AttendeeKind.External,
                null,
                null,
                AttendeeSnapshot.Create(attendee, "guest@client.test").Value,
                isRequired: true,
                _olga,
                DateTime.UtcNow,
                canManageAll: false
            );

        await using var db = _database.NewContext();
        db.Appointments.Add(appointment);
        await db.SaveChangesAsync();
        return appointment.Id;
    }
}
