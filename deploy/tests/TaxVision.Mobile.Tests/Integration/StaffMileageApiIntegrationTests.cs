using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Mobile.Application.Staff;
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Mobile.Tests.Integration;

/// <summary>
/// Rutas de lectura del personal con SQL y Wolverine reales: permiso mileage.view, cliente asignado (o
/// customers.view_all), 404 sin revelar existencia, y registro de acceso al ver un trazado o exportar.
/// </summary>
public sealed class StaffMileageApiIntegrationTests
{
    private const string Key = "mobile-staff-mileage-tests-signing-key-only-at-least-sixty-four-chars";
    private static readonly string[] Portal =
    [
        MobilePermissions.TripsRead,
        MobilePermissions.TripsManage,
        MobilePermissions.TripsSync,
        MobilePermissions.ConfigurationRead,
        MobilePermissions.VehiclesRead,
        MobilePermissions.VehiclesManage,
    ];

    [Fact]
    public async Task Staff_read_routes_follow_permission_assignment_and_tenant()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var portalUser = Guid.NewGuid();
        var assigned = Guid.NewGuid();
        var notAssigned = Guid.NewGuid();
        var viewAll = Guid.NewGuid();
        var withoutPermission = Guid.NewGuid();
        await Seed(tenant, portalUser, Portal);
        await Seed(tenant, assigned, [MileagePermissions.View]);
        await Seed(tenant, notAssigned, [MileagePermissions.View]);
        await Seed(tenant, viewAll, [MileagePermissions.View, CustomersPermissions.ViewAll]);
        await Seed(tenant, withoutPermission, []);
        await Assign(tenant, customer, assigned);
        await Assign(tenant, customer, withoutPermission);

        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tripId = await RecordTrip(client, tenant, customer, portalUser);
        var year = DateTime.UtcNow.Year;
        var basePath = $"/mobile/staff/customers/{customer}";
        string[] routes =
        [
            $"{basePath}/trips?taxYear={year}",
            $"{basePath}/trips/{tripId}",
            $"{basePath}/trips/{tripId}/points",
            $"{basePath}/vehicles",
            $"{basePath}/mileage-summary?taxYear={year}",
            $"{basePath}/trips/export?taxYear={year}",
        ];

        Token(client, tenant, assigned, "TenantEmployee");
        foreach (var route in routes)
        {
            using var ok = await client.GetAsync(route);
            Assert.True(
                ok.StatusCode == HttpStatusCode.OK,
                $"{route}: {ok.StatusCode} {await ok.Content.ReadAsStringAsync()}"
            );
            Assert.Equal("no-store", ok.Headers.CacheControl?.ToString());
        }
        var list = await client.GetFromJsonAsync<PagedResult<StaffTripResponse>>(routes[0]);
        var trip = Assert.Single(list!.Items);
        Assert.Equal(tripId, trip.Id);
        Assert.False(trip.RouteRemoved);
        var summary = await client.GetFromJsonAsync<StaffMileageSummaryResponse>(routes[4]);
        Assert.Equal(1, summary!.CompletedTrips);
        Assert.Equal(12, summary.ByMonth.Count);
        var csv = await client.GetAsync(routes[5]);
        Assert.Equal("text/csv", csv.Content.Headers.ContentType?.MediaType);
        Assert.DoesNotContain(customer.ToString(), csv.Content.Headers.ContentDisposition?.FileName ?? "");

        Token(client, tenant, notAssigned, "TenantEmployee");
        foreach (var route in routes)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);

        // Alguien de otra oficina, con el permiso en la suya, no ve al cliente de esta.
        var otherTenant = Guid.NewGuid();
        await Seed(otherTenant, assigned, [MileagePermissions.View]);
        Token(client, otherTenant, assigned, "TenantEmployee");
        foreach (var route in routes)
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);

        Token(client, tenant, withoutPermission, "TenantEmployee");
        foreach (var route in routes)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(route)).StatusCode);

        Token(client, tenant, viewAll, "TenantAdmin");
        foreach (var route in routes)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(route)).StatusCode);

        Token(client, tenant, portalUser, "CustomerPortal", customer);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(routes[0])).StatusCode);

        await using var db = Db();
        var log = await db.StaffMileageAccessLogs.IgnoreQueryFilters().Where(x => x.TenantId == tenant).ToListAsync();
        // Trazados vistos: asignado y view_all (uno cada uno). Exportaciones: el asignado dos veces y view_all una.
        // Listar, el detalle, los vehículos y el resumen no dejan rastro.
        Assert.Equal(5, log.Count);
        Assert.Equal(2, log.Count(x => x.Action == StaffMileageAccessLog.RouteViewed && x.TripId == tripId));
        Assert.Equal(3, log.Count(x => x.Action == StaffMileageAccessLog.TripsExported));
        Assert.Equal(3, log.Count(x => x.StaffUserId == assigned));
    }

    [Fact]
    public async Task A_huge_page_number_is_rejected_not_a_server_error()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var staff = Guid.NewGuid();
        await Seed(tenant, staff, [MileagePermissions.View]);
        await Assign(tenant, customer, staff);

        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Token(client, tenant, staff, "TenantEmployee");

        using var response = await client.GetAsync(
            $"/mobile/staff/customers/{customer}/trips?page={int.MaxValue}&pageSize=100"
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reading_a_route_from_any_offset_is_logged()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var portalUser = Guid.NewGuid();
        var staff = Guid.NewGuid();
        await Seed(tenant, portalUser, Portal);
        await Seed(tenant, staff, [MileagePermissions.View]);
        await Assign(tenant, customer, staff);

        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var tripId = await RecordTrip(client, tenant, customer, portalUser);
        Token(client, tenant, staff, "TenantEmployee");

        using var page = await client.GetAsync($"/mobile/staff/customers/{customer}/trips/{tripId}/points?after=0");

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        await using var db = Db();
        Assert.Equal(
            1,
            await db
                .StaffMileageAccessLogs.IgnoreQueryFilters()
                .CountAsync(x => x.TenantId == tenant && x.Action == StaffMileageAccessLog.RouteViewed)
        );
    }

    [Fact]
    public async Task The_portal_routes_keep_rejecting_staff()
    {
        var tenant = Guid.NewGuid();
        var staff = Guid.NewGuid();
        await Seed(tenant, staff, [MileagePermissions.View, MobilePermissions.TripsRead]);
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });

        Token(client, tenant, staff, "TenantEmployee", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/mobile/trips")).StatusCode);
    }

    private static async Task<Guid> RecordTrip(HttpClient client, Guid tenant, Guid customer, Guid portalUser)
    {
        Token(client, tenant, portalUser, "CustomerPortal", customer);
        var vehicle = (
            await (
                await client.PostAsJsonAsync("/mobile/vehicles", new VehicleInput("STAFF-1"))
            ).Content.ReadFromJsonAsync<VehicleResponse>()
        )!;
        var at = new DateTimeOffset(DateTime.UtcNow.AddMinutes(-5));
        var start = await client.PostAsJsonAsync(
            "/mobile/trips",
            new StartTripInput(Guid.NewGuid(), vehicle.Id, "business", at, new(0, at, 18.48, -69.84, 5))
        );
        Assert.True(start.StatusCode == HttpStatusCode.Created, await start.Content.ReadAsStringAsync());
        var id = (await start.Content.ReadFromJsonAsync<TripResponse>())!.Id;
        var complete = await client.PostAsJsonAsync(
            $"/mobile/trips/{id}/complete",
            new TrackingInput(2, at.AddSeconds(60), [new(1, at.AddSeconds(60), 18.49, -69.84, 5)])
        );
        Assert.True(complete.StatusCode == HttpStatusCode.OK, await complete.Content.ReadAsStringAsync());
        return id;
    }

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", MobileIntegrationDatabase.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:Secret", Key);
            // La reconciliación real llamaría a Customer; aquí las asignaciones se siembran a mano.
            builder.UseSetting("CustomerVisibility:Reconciliation:ReconciliationEnabled", "false");
            builder.ConfigureTestServices(s =>
            {
                s.DisableAllExternalWolverineTransports();
                s.RemoveAll<IRateCounter>();
                s.AddSingleton<IRateCounter, Counter>();
                // Sin Redis: el gate de módulo lee la proyección en SQL (sin fila → no deniega).
                s.RemoveAll<BuildingBlocks.Caching.ICacheService>();
                s.AddSingleton<BuildingBlocks.Caching.ICacheService, NoOpCache>();
            });
        });

    private static MobileDbContext Db() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>()
                .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

    private static async Task Seed(Guid tenant, Guid user, IReadOnlyList<string> codes)
    {
        await using var db = Db();
        db.UserPermissionsProjections.Add(
            UserPermissionsProjection.Create(tenant, user, 1, codes, [], DateTime.UtcNow)
        );
        await db.SaveChangesAsync();
    }

    private static async Task Assign(Guid tenant, Guid customer, Guid user)
    {
        await using var db = Db();
        db.Set<CustomerAssignmentProjection>()
            .Add(CustomerAssignmentProjection.Create(tenant, customer, user, DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static void Token(HttpClient client, Guid tenant, Guid user, string actor, Guid? customer = null)
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new("sub", user.ToString()),
            new("tenant_id", tenant.ToString()),
            new("actor_type", actor),
            new("perm_v", "1"),
        };
        if (customer.HasValue)
            claims.Add(new("customer_id", customer.Value.ToString()));
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private sealed class NoOpCache : BuildingBlocks.Caching.ICacheService
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }

    private sealed class Counter : IRateCounter
    {
        public Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default) =>
            Task.FromResult(1L);
    }
}
