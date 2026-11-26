using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Authorization;
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
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Mobile.Tests.Integration;

public sealed class VehicleWolverineIntegrationTests
{
    [Fact]
    public async Task Real_wolverine_transactions_preserve_default_when_create_conflicts()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var user = Guid.NewGuid();
        await using (
            var db = new MobileDbContext(
                new DbContextOptionsBuilder<MobileDbContext>()
                    .UseSqlServer(MobileIntegrationDatabase.ConnectionString)
                    .Options,
                new NoTenant()
            )
        )
        {
            db.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(
                    tenant,
                    user,
                    1,
                    [MobilePermissions.VehiclesRead, MobilePermissions.VehiclesManage],
                    [],
                    DateTime.UtcNow
                )
            );
            await db.SaveChangesAsync();
        }
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("ConnectionStrings:Default", MobileIntegrationDatabase.ConnectionString);
            builder.UseSetting("ConnectionStrings:Redis", "127.0.0.1:1,abortConnect=false");
            builder.UseSetting("RabbitMq:Uri", "amqp://guest:guest@127.0.0.1:1");
            builder.UseSetting("Jwt:Secret", Key);
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                services.RemoveAll<IRateCounter>();
                services.AddSingleton<IRateCounter, Counter>();
                // Sin Redis: el gate de módulo lee la proyección en SQL (sin fila → no deniega).
                services.RemoveAll<BuildingBlocks.Caching.ICacheService>();
                services.AddSingleton<BuildingBlocks.Caching.ICacheService, NoOpCache>();
            });
        });
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var claims = new[]
        {
            new Claim("sub", user.ToString()),
            new Claim("tenant_id", tenant.ToString()),
            new Claim("customer_id", customer.ToString()),
            new Claim("actor_type", "CustomerPortal"),
            new Claim("perm_v", "1"),
        };
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
        var created = await client.PostAsJsonAsync("/mobile/vehicles", new VehicleInput("ORIGINAL", IsDefault: true));
        Assert.True(created.StatusCode == HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var original = (await created.Content.ReadFromJsonAsync<VehicleResponse>())!;
        var duplicate = await client.PostAsJsonAsync("/mobile/vehicles", new VehicleInput("ORIGINAL", IsDefault: true));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var loaded = await client.GetFromJsonAsync<VehicleResponse>($"/mobile/vehicles/{original.Id}");
        Assert.True(loaded!.IsDefault);
        var second = await client.PostAsJsonAsync("/mobile/vehicles", new VehicleInput("SECOND", IsDefault: true));
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        loaded = await client.GetFromJsonAsync<VehicleResponse>($"/mobile/vehicles/{original.Id}");
        Assert.False(loaded!.IsDefault);
        Assert.Equal(
            HttpStatusCode.NoContent,
            (await client.DeleteAsync($"/mobile/vehicles/{original.Id}")).StatusCode
        );
    }

    private const string Key = "mobile-sql-tests-signing-key-only-at-least-sixty-four-characters-long";

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
