using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using BuildingBlocks.Authorization;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Permissions;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Sessions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using TaxVision.Mobile.Application.Vehicles;
using TaxVision.Mobile.Application.Vehicles.Commands;
using TaxVision.Mobile.Application.Vehicles.Queries;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;
using TaxVision.Mobile.Tests.Architecture;
using Wolverine;

namespace TaxVision.Mobile.Tests.Api;

public sealed class VehicleHttpTests
{
    [Fact]
    public async Task Http_crud_uses_claims_and_returns_rest_statuses()
    {
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        Token(client, t, c);
        var create = await client.PostAsJsonAsync(
            "/mobile/vehicles",
            new
            {
                plate = "ABC",
                tenantId = Guid.NewGuid(),
                customerId = Guid.NewGuid(),
                isDefault = true,
            }
        );
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var vehicle = (await create.Content.ReadFromJsonAsync<VehicleResponse>())!;
        Assert.EndsWith($"/mobile/vehicles/{vehicle.Id}", create.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(create.Headers.Location)).StatusCode);
        Token(client, t, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(create.Headers.Location)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await client.PutAsJsonAsync(create.Headers.Location, new { plate = "HIDDEN" })).StatusCode
        );
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync(create.Headers.Location)).StatusCode);
        Token(client, Guid.NewGuid(), c);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(create.Headers.Location)).StatusCode);
        Token(client, t, c);
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync(create.Headers.Location, new { plate = " " })).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.OK,
            (await client.PutAsJsonAsync(create.Headers.Location, new { plate = "NEW" })).StatusCode
        );
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/mobile/vehicles?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(create.Headers.Location)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(create.Headers.Location)).StatusCode);
    }

    [Fact]
    public async Task Anonymous_missing_identity_wrong_actor_and_missing_permission_are_denied()
    {
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mobile/vehicles")).StatusCode);
        var t = Guid.NewGuid();
        var c = Guid.NewGuid();
        Token(client, t, c, actor: "TenantEmployee");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/mobile/vehicles")).StatusCode);
        Token(client, t, c, hasPermissions: false);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/mobile/vehicles")).StatusCode);
        Token(client, t, null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mobile/vehicles")).StatusCode);
    }

    [Fact]
    public async Task Denylisted_session_is_rejected()
    {
        using var factory = Factory();
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        Token(client, Guid.NewGuid(), Guid.NewGuid(), sid: Guid.NewGuid());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/mobile/vehicles")).StatusCode);
    }

    [Fact]
    public async Task Enforced_mileage_gate_denies_without_the_module_and_lets_in_with_it()
    {
        using var factory = Factory(enforceMileage: true);
        using var client = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        var withMileage = Guid.NewGuid();
        var withoutModules = Guid.NewGuid();
        var noPlanYet = Guid.NewGuid();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MobileDbContext>();
            var included = TaxVision.Mobile.Domain.RateLimiting.TenantPlanCodeProjection.Create(
                withMileage,
                "enterprise",
                1
            );
            included.ApplyIfNewer("enterprise", 1, ["mileage"]);
            var expired = TaxVision.Mobile.Domain.RateLimiting.TenantPlanCodeProjection.Create(
                withoutModules,
                "enterprise",
                1
            );
            expired.ApplyIfNewer("enterprise", 1, []);
            db.TenantPlanCodeProjections.AddRange(included, expired);
            await db.SaveChangesAsync();
        }

        Token(client, withMileage, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mobile/vehicles")).StatusCode);

        // [] = se sabe que no tiene ningún módulo: deniega con el código que traduce el portal.
        Token(client, withoutModules, Guid.NewGuid());
        var denied = await client.GetAsync("/mobile/vehicles");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Contains("Authz.ModuleUnavailable", await denied.Content.ReadAsStringAsync());

        // Sin fila todavía (consistencia eventual): no deniega.
        Token(client, noPlanYet, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/mobile/vehicles")).StatusCode);
    }

    private const string Key = "mobile-tests-only-signing-key-64-characters-long-do-not-use-in-prod";

    private static WebApplicationFactory<Program> Factory(bool enforceMileage = false)
    {
        var database = Guid.NewGuid().ToString();
        return new MobileRoutingHostFactory().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Jwt:Secret", Key);
            if (enforceMileage)
            {
                builder.UseSetting("Authorization:ModuleGate:Enforce", "true");
                builder.UseSetting("Authorization:ModuleGate:EnforcedModules:0", "mileage");
            }
            builder.UseSetting("Wolverine:CodeGeneration:TypeLoadMode", "Dynamic");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<MobileDbContext>>();
                services.RemoveAll<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration<MobileDbContext>>();
                services.AddDbContext<MobileDbContext>(o =>
                    o.UseInMemoryDatabase(database)
                        .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                );
                services.RemoveAll<IMessageBus>();
                services.AddScoped<IMessageBus>(sp =>
                {
                    var bus = DispatchProxy.Create<IMessageBus, DirectBus>();
                    ((DirectBus)(object)bus).Db = sp.GetRequiredService<MobileDbContext>();
                    return bus;
                });
                services.RemoveAll<IUserPermissionsProjectionReader>();
                services.AddSingleton<IUserPermissionsProjectionReader, Reader>();
                services.RemoveAll<IPermissionsSnapshotClient>();
                services.RemoveAll<IRateCounter>();
                services.AddSingleton<IRateCounter, Counter>();
                services.RemoveAll<ISessionDenylistReader>();
                services.AddSingleton<ISessionDenylistReader, Denylist>();
                // Sin Redis: el gate de mileage lee la proyección en memoria (sin fila → no deniega).
                services.RemoveAll<BuildingBlocks.Caching.ICacheService>();
                services.AddSingleton<BuildingBlocks.Caching.ICacheService, NoOpCache>();
            });
        });
    }

    private static void Token(
        HttpClient client,
        Guid tenant,
        Guid? customer,
        string actor = "CustomerPortal",
        bool hasPermissions = true,
        Guid? sid = null
    )
    {
        var claims = new List<Claim>
        {
            new("sub", Guid.NewGuid().ToString()),
            new("tenant_id", tenant.ToString()),
            new("actor_type", actor),
            new("perm_v", "1"),
        };
        if (customer.HasValue)
            claims.Add(new("customer_id", customer.Value.ToString()));
        if (sid.HasValue)
            claims.Add(new("sid", sid.Value.ToString()));
        Reader.Allowed[Guid.Parse(claims[0].Value)] = hasPermissions;
        var jwt = new JwtSecurityToken(
            "TaxVision.Auth",
            "TaxVision.Services",
            claims,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
                SecurityAlgorithms.HmacSha256
            )
        );
        client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(jwt));
    }

    public class DirectBus : DispatchProxy
    {
        public MobileDbContext Db { get; set; } = null!;

        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            if (method?.Name != "InvokeAsync")
                return null;
            var repository = new VehicleRepository(Db);
            var ct = args!.OfType<CancellationToken>().FirstOrDefault();
            return args![0] switch
            {
                CreateVehicleCommand cmd => CreateVehicleHandler.Handle(cmd, repository, Db, TimeProvider.System, ct),
                UpdateVehicleCommand cmd => UpdateVehicleHandler.Handle(cmd, repository, Db, TimeProvider.System, ct),
                DeleteVehicleCommand cmd => DeleteVehicleHandler.Handle(
                    cmd,
                    repository,
                    Db,
                    new TripRepository(Db),
                    ct
                ),
                GetVehicleQuery query => GetVehicleHandler.Handle(query, repository, ct),
                ListVehiclesQuery query => ListVehiclesHandler.Handle(query, repository, ct),
                _ => throw new NotSupportedException(),
            };
        }
    }

    private sealed class Reader : IUserPermissionsProjectionReader
    {
        public static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> Allowed = new();

        public Task<UserPermissionsSnapshot?> GetSnapshotAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<UserPermissionsSnapshot?>(
                new(
                    1,
                    Allowed.GetValueOrDefault(userId)
                        ? [MobilePermissions.VehiclesRead, MobilePermissions.VehiclesManage]
                        : []
                )
            );
    }

    private sealed class Counter : IRateCounter
    {
        public Task<long> IncrementAndGetAsync(RateCounterKey key, TimeSpan window, CancellationToken ct = default) =>
            Task.FromResult(1L);
    }

    private sealed class Denylist : ISessionDenylistReader
    {
        public Task<bool> IsSessionDeniedAsync(Guid id, CancellationToken ct = default) => Task.FromResult(true);
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
}
