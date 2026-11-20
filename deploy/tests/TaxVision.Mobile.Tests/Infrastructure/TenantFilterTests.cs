using BuildingBlocks.Domain;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Domain.Vehicles;
using TaxVision.Mobile.Infrastructure.Persistence;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>Filtro global por oficina: A no ve lo de B y, sin oficina en contexto, no se ve nada.</summary>
public sealed class TenantFilterTests
{
    private static readonly DateTime Now = new(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task One_office_does_not_see_the_data_of_another()
    {
        var database = Guid.NewGuid().ToString();
        var officeA = Guid.NewGuid();
        var officeB = Guid.NewGuid();
        await SeedAsync(database, officeA, "AAA");
        await SeedAsync(database, officeB, "BBB");

        await using var asA = Context(database, officeA);

        Assert.Equal(["AAA"], await asA.Vehicles.Select(v => v.Details.Plate).ToListAsync());
        Assert.All(await asA.Trips.ToListAsync(), t => Assert.Equal(officeA, t.TenantId));
        Assert.Single(await asA.Trips.ToListAsync());
        Assert.All(await asA.TripPoints.ToListAsync(), p => Assert.Equal(officeA, p.TenantId));
        Assert.Single(await asA.TripPoints.ToListAsync());
        Assert.Equal(officeA, (await asA.MileageRates.SingleAsync()).TenantId);
        Assert.Equal(officeA, (await asA.MileageRetentionPolicies.SingleAsync()).TenantId);
    }

    // Fail-closed: un job o un consumer que olvide fijar la oficina no lee datos de ninguna.
    [Fact]
    public async Task Without_an_office_in_context_nothing_is_visible()
    {
        var database = Guid.NewGuid().ToString();
        await SeedAsync(database, Guid.NewGuid(), "AAA");

        await using var noOffice = Context(database, tenant: null);

        Assert.Empty(await noOffice.Vehicles.ToListAsync());
        Assert.Empty(await noOffice.Trips.ToListAsync());
        Assert.Empty(await noOffice.TripPoints.ToListAsync());
        Assert.Empty(await noOffice.MileageRates.ToListAsync());
        Assert.Empty(await noOffice.MileageRetentionPolicies.ToListAsync());
        Assert.Single(await noOffice.Vehicles.IgnoreQueryFilters().ToListAsync());
    }

    // Una entidad nueva con oficina no puede quedar fuera del filtro.
    [Fact]
    public void Every_tenant_owned_entity_has_the_global_filter()
    {
        using var db = Context(Guid.NewGuid().ToString(), tenant: null);

        var owned = db.Model.GetEntityTypes().Where(e => typeof(ITenantOwned).IsAssignableFrom(e.ClrType)).ToList();

        Assert.NotEmpty(owned);
        Assert.Empty(owned.Where(e => e.GetDeclaredQueryFilters().Count == 0).Select(e => e.ClrType.Name));
    }

    private static async Task SeedAsync(string database, Guid tenant, string plate)
    {
        await using var db = Context(database, tenant);
        var customer = Guid.NewGuid();
        var start = Now.AddHours(-1);
        var vehicle = Vehicle
            .Create(tenant, customer, VehicleDetails.Create(plate, null, null, null, null, null, 0).Value, false, Now)
            .Value;
        var first = new GpsSample(0, start, 40, -74, 5);
        var trip = Trip.Start(
            tenant,
            customer,
            Guid.NewGuid(),
            vehicle.Id,
            plate,
            "business",
            null,
            start,
            first,
            Now
        ).Value;
        db.Vehicles.Add(vehicle);
        db.Trips.Add(trip);
        db.TripPoints.Add(TripPoint.From(trip, first));
        db.MileageRates.Add(MileageRate.Create(tenant, "business", DateOnly.FromDateTime(Now), 0.5m, "USD", Now).Value);
        db.MileageRetentionPolicies.Add(MileageRetentionPolicy.Create(tenant, 30, Now).Value);
        await db.SaveChangesAsync();
    }

    private static MobileDbContext Context(string database, Guid? tenant) =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(database).Options,
            new FixedTenant(tenant)
        );

    private sealed class FixedTenant(Guid? tenant) : ITenantContext
    {
        public Guid TenantId => tenant ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => tenant.HasValue;

        public void SetTenant(Guid tenantId) { }
    }
}
