using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Authorization;
using BuildingBlocks.Caching;
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
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Domain.Trips;
using TaxVision.Mobile.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Mobile.Tests.Integration;

/// <summary>GET/PUT del plazo de retención de GPS con SQL y Wolverine reales (sin Redis: la caché es un doble).</summary>
public sealed class MileageRetentionApiIntegrationTests
{
    private const string Key = "mobile-retention-tests-signing-key-only-at-least-sixty-four-characters";
    private const string Route = "/mobile/mileage-configuration/retention";

    [Fact]
    public async Task Retention_is_read_and_changed_with_the_same_response_shape()
    {
        var tenant = Guid.NewGuid();
        using var factory = Factory();
        using var client = await AdminClientAsync(factory, tenant);

        var initial = await client.GetFromJsonAsync<JsonElement>(Route);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("retentionDays").ValueKind);
        Assert.False(initial.GetProperty("configured").GetBoolean());
        Assert.Equal(
            MileageRetentionPolicy.DefaultRetentionDays,
            initial.GetProperty("effectiveRetentionDays").GetInt32()
        );

        var put = await client.PutAsJsonAsync(Route, new { retentionDays = 30 });
        Assert.True(put.StatusCode == HttpStatusCode.OK, await put.Content.ReadAsStringAsync());
        var changed = await put.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(30, changed.GetProperty("retentionDays").GetInt32());
        Assert.True(changed.GetProperty("configured").GetBoolean());
        Assert.Equal(30, changed.GetProperty("effectiveRetentionDays").GetInt32());

        var reloaded = await client.GetFromJsonAsync<JsonElement>(Route);
        Assert.Equal(30, reloaded.GetProperty("retentionDays").GetInt32());

        // Vaciar el plazo vuelve al por defecto; no existe "sin purga".
        var cleared = await client.PutAsJsonAsync(Route, new { retentionDays = (int?)null });
        Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);
        var afterClear = await client.GetFromJsonAsync<JsonElement>(Route);
        Assert.False(afterClear.GetProperty("configured").GetBoolean());
        Assert.Equal(
            MileageRetentionPolicy.DefaultRetentionDays,
            afterClear.GetProperty("effectiveRetentionDays").GetInt32()
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(3651)]
    public async Task An_out_of_range_period_is_rejected_with_the_standard_error(int days)
    {
        var tenant = Guid.NewGuid();
        using var factory = Factory();
        using var client = await AdminClientAsync(factory, tenant);

        var response = await client.PutAsJsonAsync(Route, new { retentionDays = days });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Mobile.Retention.InvalidDays", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
        await using var db = Db();
        Assert.False(await db.MileageRetentionPolicies.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenant));
    }

    // Varios PUT a la vez sobre una oficina sin fila: ninguno da 500 y queda una sola fila válida.
    [Fact]
    public async Task Simultaneous_first_writes_never_fail_with_a_server_error()
    {
        var tenant = Guid.NewGuid();
        using var factory = Factory();
        using var client = await AdminClientAsync(factory, tenant);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(i => client.PutAsJsonAsync(Route, new { retentionDays = 30 + i }))
        );

        Assert.All(
            responses,
            r => Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict, $"Got {(int)r.StatusCode}.")
        );
        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.OK);
        await using var db = Db();
        var policy = await db.MileageRetentionPolicies.IgnoreQueryFilters().SingleAsync(p => p.TenantId == tenant);
        Assert.InRange(policy.RetentionDays!.Value, 30, 35);
    }

    [Fact]
    public async Task One_office_does_not_read_or_change_the_period_of_another()
    {
        var tenant = Guid.NewGuid();
        var other = Guid.NewGuid();
        using var factory = Factory();
        using var client = await AdminClientAsync(factory, tenant);
        using var foreign = await AdminClientAsync(factory, other);

        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync(Route, new { retentionDays = 45 })).StatusCode);

        var seenByOther = await foreign.GetFromJsonAsync<JsonElement>(Route);
        Assert.False(seenByOther.GetProperty("configured").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, (await foreign.PutAsJsonAsync(Route, new { retentionDays = 90 })).StatusCode);
        var mine = await client.GetFromJsonAsync<JsonElement>(Route);
        Assert.Equal(45, mine.GetProperty("retentionDays").GetInt32());
    }

    private static async Task<HttpClient> AdminClientAsync(WebApplicationFactory<Program> factory, Guid tenant)
    {
        var admin = Guid.NewGuid();
        await using (var db = Db())
        {
            db.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(tenant, admin, 1, [MobilePermissions.RatesManage], [], DateTime.UtcNow)
            );
            await db.SaveChangesAsync();
        }
        var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            [
                new("sub", admin.ToString()),
                new("tenant_id", tenant.ToString()),
                new("actor_type", "TenantAdmin"),
                new("perm_v", "1"),
            ],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
        return client;
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
                // Sin Redis: el plan del gate se lee directo de la proyección.
                s.RemoveAll<ICacheService>();
                s.AddSingleton<ICacheService, NoCache>();
            });
        });

    private static MobileDbContext Db() =>
        new(
            new DbContextOptionsBuilder<MobileDbContext>()
                .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                .Options,
            new NoTenant()
        );

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

    private sealed class NoCache : ICacheService
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
}
