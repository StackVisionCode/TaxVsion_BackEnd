using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
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
using TaxVision.Mobile.Application.Trips;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Mobile.Tests.Integration;

public sealed class TripApiIntegrationTests
{
    private const string Key = "mobile-tracking-tests-signing-key-only-at-least-sixty-four-characters";
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
    public async Task Tracking_lifecycle_persists_and_replays_with_real_sql_and_wolverine()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var portal = Guid.NewGuid();
        var admin = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var at = new DateTimeOffset(now);
        await Seed(tenant, portal, Portal);
        await Seed(tenant, admin, [MobilePermissions.ConfigurationRead, MobilePermissions.RatesManage]);
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Token(client, tenant, customer, admin, "TenantAdmin");
        var rateResponse = await client.PostAsJsonAsync(
            "/mobile/mileage-configuration/rates",
            new CreateRateInput("business", DateOnly.FromDateTime(now), 0.1234m, "USD")
        );
        Assert.True(rateResponse.StatusCode == HttpStatusCode.Created, await rateResponse.Content.ReadAsStringAsync());
        var rate = (await rateResponse.Content.ReadFromJsonAsync<MileageRateResponse>())!;
        Assert.Equal(
            HttpStatusCode.Conflict,
            (
                await client.PostAsJsonAsync(
                    "/mobile/mileage-configuration/rates",
                    new CreateRateInput("business", DateOnly.FromDateTime(now), 0.5m, "USD")
                )
            ).StatusCode
        );
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mobile/mileage-configuration/rates")).StatusCode);
        Token(client, tenant, customer, portal, "CustomerPortal");
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await client.PostAsJsonAsync(
                    "/mobile/mileage-configuration/rates",
                    new CreateRateInput("business", DateOnly.FromDateTime(now), 1m, "USD")
                )
            ).StatusCode
        );
        var config = await client.GetFromJsonAsync<MileageConfigurationResponse>(
            "/mobile/mileage-configuration?purpose=business"
        );
        Assert.Equal(rate.Id, config!.RateId);
        var vehicleCreate = await client.PostAsJsonAsync("/mobile/vehicles", new VehicleInput("TRIP-TEST"));
        var vehicle = (await vehicleCreate.Content.ReadFromJsonAsync<VehicleResponse>())!;
        var input = new StartTripInput(Guid.NewGuid(), vehicle.Id, "business", at, new(0, at, 40, -74, 5));
        var start = await client.PostAsJsonAsync("/mobile/trips", input);
        Assert.True(start.StatusCode == HttpStatusCode.Created, await start.Content.ReadAsStringAsync());
        var trip = (await start.Content.ReadFromJsonAsync<TripResponse>())!;
        var id = trip.Id;
        var retry = await client.PostAsJsonAsync("/mobile/trips", input);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True((await retry.Content.ReadFromJsonAsync<TripResponse>())!.Replayed);
        var changedStart = await client.PostAsJsonAsync("/mobile/trips", input with { Purpose = "medical" });
        Assert.Equal(HttpStatusCode.Conflict, changedStart.StatusCode);
        var sync = new TrackingInput(2, at.AddSeconds(5), [new(1, at.AddSeconds(5), 40.01, -74, 5, 10)]);
        await Expect(client.PutAsJsonAsync($"/mobile/trips/{id}/tracking", sync), HttpStatusCode.OK);
        var repeated = await client.PutAsJsonAsync($"/mobile/trips/{id}/tracking", sync);
        Assert.True((await repeated.Content.ReadFromJsonAsync<TripResponse>())!.Replayed);
        await Expect(
            client.PutAsJsonAsync($"/mobile/trips/{id}/tracking", sync with { ObservedAt = at.AddSeconds(6) }),
            HttpStatusCode.Conflict
        );
        var points = await client.GetFromJsonAsync<TripPointsPage>($"/mobile/trips/{id}/points?size=1");
        Assert.Single(points!.Items);
        Assert.True(points.HasMore);
        var nextPoints = await client.GetFromJsonAsync<TripPointsPage>(
            $"/mobile/trips/{id}/points?after={points.NextAfter}&size=1"
        );
        Assert.Single(nextPoints!.Items);
        Assert.False(nextPoints.HasMore);
        await Expect(
            client.PostAsJsonAsync($"/mobile/trips/{id}/pause", new TransitionInput(3, at.AddSeconds(10))),
            HttpStatusCode.OK
        );
        await Expect(
            client.PostAsJsonAsync($"/mobile/trips/{id}/resume", new TransitionInput(4, at.AddSeconds(20))),
            HttpStatusCode.OK
        );
        var completed = await client.PostAsJsonAsync(
            $"/mobile/trips/{id}/complete",
            new TrackingInput(5, at.AddSeconds(30), [new(2, at.AddSeconds(30), 41, -74, 5)])
        );
        Assert.True(completed.StatusCode == HttpStatusCode.OK, await completed.Content.ReadAsStringAsync());
        trip = (await completed.Content.ReadFromJsonAsync<TripResponse>())!;
        Assert.Equal(TripStatus.Completed, trip.Status);
        Assert.Equal(20, trip.DurationSeconds);
        Assert.InRange(trip.DistanceMeters, 1100, 1120);
        Assert.Equal(rate.Id, trip.MileageRateId);
        Assert.NotNull(trip.EstimatedAmount);
        await Expect(
            client.PutAsJsonAsync($"/mobile/trips/{id}/tracking", new TrackingInput(6, at.AddSeconds(35), [])),
            HttpStatusCode.Conflict
        );
        var list = await client.GetFromJsonAsync<PagedResult<TripResponse>>(
            "/mobile/trips?purpose=business&status=Completed&minDistanceMiles=0.1&sortBy=distance&sortOrder=asc"
        );
        Assert.Single(list!.Items);
        var daily = await client.GetAsync("/mobile/statistics/daily?days=7&timeZone=America%2FNew_York");
        Assert.True(daily.StatusCode == HttpStatusCode.OK, await daily.Content.ReadAsStringAsync());
        var days = (await daily.Content.ReadFromJsonAsync<DailyMileageResponse[]>())!;
        Assert.Equal(7, days.Length);
        Assert.Equal(1, days.Sum(d => d.TripsCount));
        var stats = await client.GetFromJsonAsync<TripStatisticsResponse>(
            "/mobile/statistics?timeZone=America%2FNew_York"
        );
        Assert.Equal(1, stats!.TotalTrips);
        Assert.Equal("USD", Assert.Single(stats.EstimatedAmounts).Currency);
        await Expect(
            client.PatchAsJsonAsync(
                $"/mobile/trips/{id}",
                new EditTripInput(6, at.AddSeconds(40), "business", "business visit")
            ),
            HttpStatusCode.OK
        );
        var other = Guid.NewGuid();
        await Seed(tenant, other, Portal);
        Token(client, tenant, Guid.NewGuid(), other, "CustomerPortal");
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mobile/trips/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mobile/trips/{id}/points")).StatusCode);
        await Expect(
            client.PostAsJsonAsync($"/mobile/trips/{id}/cancel", new TransitionInput(7, at.AddSeconds(45))),
            HttpStatusCode.NotFound
        );
        Token(client, tenant, customer, portal, "CustomerPortal");
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/mobile/vehicles/{vehicle.Id}")).StatusCode);
        var deletion =
            $"/mobile/trips/{id}?sequence=7&observedAt={Uri.EscapeDataString(at.AddSeconds(45).ToString("O"))}";
        await Expect(client.DeleteAsync(deletion), HttpStatusCode.NoContent);
        await Expect(client.DeleteAsync(deletion), HttpStatusCode.NoContent);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/mobile/trips/{id}")).StatusCode);
        stats = await client.GetFromJsonAsync<TripStatisticsResponse>("/mobile/statistics");
        Assert.Equal(0, stats!.TotalTrips);
    }

    [Fact]
    public async Task Anonymous_missing_permissions_and_invalid_input_are_rejected()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var user = Guid.NewGuid();
        await Seed(tenant, user, []);
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mobile/trips")).StatusCode);
        Token(client, tenant, customer, user, "CustomerPortal");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/mobile/trips")).StatusCode);
        var allowed = Guid.NewGuid();
        await Seed(tenant, allowed, Portal);
        Token(client, tenant, customer, allowed, "CustomerPortal");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/mobile/trips?page=0")).StatusCode);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/mobile/statistics?timeZone=invalid")).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.GetAsync("/mobile/statistics/daily?days=367")).StatusCode
        );
        Token(client, tenant, customer, allowed, "TenantEmployee");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/mobile/trips")).StatusCode);
    }

    private static WebApplicationFactory<Program> Factory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", MobileIntegrationDatabase.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:Secret", Key);
            builder.ConfigureTestServices(s =>
            {
                s.DisableAllExternalWolverineTransports();
                s.RemoveAll<IRateCounter>();
                s.AddSingleton<IRateCounter, Counter>();
            });
        });

    private static async Task Seed(Guid t, Guid u, IReadOnlyList<string> codes)
    {
        await using var db = new MobileDbContext(
            new DbContextOptionsBuilder<MobileDbContext>()
                .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );
        db.UserPermissionsProjections.Add(UserPermissionsProjection.Create(t, u, 1, codes, [], DateTime.UtcNow));
        await db.SaveChangesAsync();
    }

    private static void Token(HttpClient c, Guid t, Guid customer, Guid user, string actor)
    {
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            [
                new("sub", user.ToString()),
                new("tenant_id", t.ToString()),
                new("customer_id", customer.ToString()),
                new("actor_type", actor),
                new("perm_v", "1"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        c.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }

    private static async Task Expect(Task<HttpResponseMessage> task, HttpStatusCode code)
    {
        using var response = await task;
        Assert.True(
            response.StatusCode == code,
            $"Expected {code}, got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
        );
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private sealed class Counter : IRateCounter
    {
        public Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default) =>
            Task.FromResult(1L);
    }
}
