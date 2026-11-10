using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Calendar.Domain.Appointments;
using TaxVision.Calendar.Domain.Booking;
using TaxVision.Calendar.Domain.ValueObjects;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// B6 contra SQL Server real: el filtro por cliente y por tenant vive en la consulta, y la clave de
/// idempotencia de la reserva es única en la base (un segundo insert con la misma clave falla).
/// </summary>
public sealed class PortalCalendarStoreIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Start = DateTime.UtcNow.Date.AddDays(10).AddHours(15);

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _clientA = Guid.NewGuid();
    private readonly Guid _clientB = Guid.NewGuid();
    private readonly Guid _olga = Guid.NewGuid();

    public Task InitializeAsync() => _database.CreateAsync();

    public Task DisposeAsync() => _database.DropAsync();

    [Fact]
    public async Task A_client_sees_their_own_and_their_invitations_but_never_another_clients_or_tenants()
    {
        var mine = await SeedAsync(_tenant, _clientA, guest: null);
        var invited = await SeedAsync(_tenant, null, guest: _clientA);
        var theirs = await SeedAsync(_tenant, _clientB, guest: null);
        var otherTenant = await SeedAsync(_otherTenant, _clientA, guest: null);

        await using var db = _database.NewContext();
        var store = new PortalCalendarStore(db);
        var listed = (await store.ListForCustomerAsync(_tenant, _clientA, Start.AddDays(-1), Start.AddDays(1)))
            .Select(a => a.Id)
            .ToList();

        Assert.Equal(new[] { mine, invited }.Order(), listed.Order());
        Assert.Null(await store.FindForCustomerAsync(_tenant, _clientA, theirs));
        Assert.Null(await store.FindForCustomerAsync(_tenant, _clientA, otherTenant));
        Assert.NotNull(await store.FindForCustomerAsync(_tenant, _clientA, invited));
    }

    [Fact]
    public async Task Assigned_preparers_come_from_the_projection_and_a_repeated_key_is_rejected_by_the_database()
    {
        await using (var db = _database.NewContext())
        {
            db.Set<CustomerAssignmentProjection>()
                .Add(CustomerAssignmentProjection.Create(_tenant, _clientA, _olga, DateTime.UtcNow));
            db.BookingRequests.Add(BookingRequest.Create(_tenant, _clientA, "key-1", Guid.NewGuid(), DateTime.UtcNow));
            await db.SaveChangesAsync();
        }

        await using (var db = _database.NewContext())
        {
            var store = new PortalCalendarStore(db);
            Assert.Equal([_olga], await store.ListAssignedStaffAsync(_tenant, _clientA));
            Assert.Empty(await store.ListAssignedStaffAsync(_tenant, _clientB));
            Assert.NotNull(await store.FindBookingAsync(_tenant, _clientA, "key-1"));
            Assert.Null(await store.FindBookingAsync(_tenant, _clientB, "key-1"));

            db.BookingRequests.Add(BookingRequest.Create(_tenant, _clientA, "key-1", Guid.NewGuid(), DateTime.UtcNow));
            // El contexto traduce el índice único a ConflictException (409), no a un 500.
            await Assert.ThrowsAsync<BuildingBlocks.Results.ConflictException>(() => db.SaveChangesAsync());
        }
    }

    private async Task<Guid> SeedAsync(Guid tenant, Guid? customer, Guid? guest)
    {
        var appointment = Appointment
            .Schedule(
                tenant,
                AppointmentTitle.Create("Tax review").Value,
                EventTiming.PointInTimeOf(Start, Start.AddMinutes(30), "America/New_York").Value,
                Guid.NewGuid(),
                _olga,
                DateTime.UtcNow,
                customerId: customer
            )
            .Value;
        if (guest is { } guestId)
            appointment.AddAttendee(
                AttendeeKind.Customer,
                null,
                guestId,
                AttendeeSnapshot.Create("Guest", "guest@client.test").Value,
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
