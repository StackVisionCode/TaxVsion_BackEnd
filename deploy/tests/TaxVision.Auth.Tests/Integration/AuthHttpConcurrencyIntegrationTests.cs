using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using JasperFx;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using Wolverine;

namespace TaxVision.Auth.Tests.Integration;

/// <summary>Host real de Auth sobre una base temporal: cómo se ve por HTTP el token de concurrencia.</summary>
public sealed class AuthHttpConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly RoleSaveBarrier _barrier = new();
    private AuthTestDatabase _database = null!;
    private TemporaryDatabaseApiFactory _factory = null!;
    private Tenant _tenant = null!;
    private Role _role = null!;
    private User _user = null!;

    public async Task InitializeAsync()
    {
        _database = await AuthTestDatabase.CreateAsync();
        _tenant = Tenant
            .Register(
                Guid.NewGuid(),
                $"t{Guid.NewGuid():N}",
                "Office",
                BuildingBlocks.Tenancy.TenantKind.Customer,
                "America/New_York"
            )
            .Value;
        _role = Role.Create(_tenant.Id, "Reviewers", null).Value;
        _user = User.Register(
            _tenant.Id,
            "Ana",
            "Test",
            "ana@example.test",
            "not-a-real-hash",
            UserActorType.TenantEmployee
        ).Value;

        await using var db = _database.NewContext(_tenant.Id);
        db.Add(_tenant);
        db.Roles.Add(_role);
        db.Users.Add(_user);
        await db.SaveChangesAsync();

        _factory = new TemporaryDatabaseApiFactory(_database.ConnectionString, _barrier);
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Login_WrongPassword_CountsTheFailureInsideTheHandlerTransaction()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync(
            "/auth/login",
            new
            {
                Email = "ana@example.test",
                Password = "wrong",
                TenantId = _tenant.Id,
            }
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await using var read = _database.NewContext(_tenant.Id);
        Assert.Equal(1, (await read.Users.AsNoTracking().SingleAsync(user => user.Id == _user.Id)).FailedLoginCount);
    }

    [Fact]
    public async Task RolesController_ConcurrentUpdate_Returns409()
    {
        var token = JwtTestTokenFactory.Mint(_factory.JwtSecret, _tenant.Id, _user.Id);

        var responses = await Task.WhenAll(PutRoleAsync(token, "First"), PutRoleAsync(token, "Second"));

        Assert.Single(responses, response => response.StatusCode == HttpStatusCode.NoContent);
        var conflict = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        Assert.Contains("Persistence.ConcurrencyConflict", await conflict.Content.ReadAsStringAsync());
        // Sin reintento: cada PUT guardó una sola vez.
        Assert.Equal(2, _barrier.Arrivals);
    }

    private Task<HttpResponseMessage> PutRoleAsync(string token, string name)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.PutAsJsonAsync($"/auth/roles/{_role.Id}", new { Name = name, Description = (string?)null });
    }

    /// <summary>Retiene el primer guardado de cada PUT hasta que los dos leyeron el rol.</summary>
    private sealed class RoleSaveBarrier : SaveChangesInterceptor
    {
        private readonly Barrier _barrier = new(2);
        private int _arrivals;

        public int Arrivals => _arrivals;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var editsRole = eventData.Context!.ChangeTracker.Entries<Role>().Any(e => e.State == EntityState.Modified);
            if (editsRole && Interlocked.Increment(ref _arrivals) <= 2)
                _barrier.SignalAndWait(TimeSpan.FromSeconds(30), cancellationToken);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class AllowAllPermissions : IAuthorizationHandler
    {
        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            foreach (var requirement in context.PendingRequirements.ToList())
                context.Succeed(requirement);
            return Task.CompletedTask;
        }
    }

    private sealed class TemporaryDatabaseApiFactory(string connectionString, RoleSaveBarrier barrier)
        : WebApplicationFactory<Program>
    {
        public string JwtSecret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration(
                (_, config) =>
                    config.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["Jwt:PublicKeyPath"] = string.Empty,
                            ["Jwt:PublicKeyPem"] = string.Empty,
                            ["Jwt:Secret"] = JwtSecret,
                        }
                    )
            );
            builder.ConfigureTestServices(services =>
            {
                services.DisableAllExternalWolverineTransports();
                services.ConfigureWolverine(options =>
                {
                    options.Durability.Mode = DurabilityMode.MediatorOnly;
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
                });
                foreach (var hosted in services.Where(d => d.ServiceType == typeof(IHostedService)).ToList())
                {
                    if (
                        hosted.ImplementationType?.Namespace?.StartsWith("TaxVision.", StringComparison.Ordinal) == true
                    )
                        services.Remove(hosted);
                }
                services.ConfigureDbContext<AuthDbContext>(db =>
                    db.UseSqlServer(connectionString).AddInterceptors(barrier)
                );
                services.AddSingleton<IAuthorizationHandler, AllowAllPermissions>();
            });
        }
    }
}
