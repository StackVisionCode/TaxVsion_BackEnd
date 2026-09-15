using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Api.Jobs;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>Anti-entropy periódico (§44): un pase re-publica UserRolesChanged para cada usuario ACTIVO
/// (sin cursor persistido, keyset por Id, cross-tenant), y omite a los desactivados.</summary>
public sealed class PermissionsReconciliationServiceTests
{
    [Fact]
    public async Task Republishes_one_event_per_active_user_and_skips_inactive()
    {
        var (provider, bus) = BuildProvider(Guid.NewGuid().ToString());

        var activeA = User.Register(Guid.NewGuid(), "Ada", "Lovelace", "ada@acme.com", "hash", UserActorType.TenantAdmin)
            .Value;
        var activeB = User.Register(Guid.NewGuid(), "Grace", "Hopper", "grace@acme.com", "hash", UserActorType.TenantEmployee)
            .Value;
        var inactive = User.Register(Guid.NewGuid(), "Alan", "Turing", "alan@acme.com", "hash", UserActorType.TenantEmployee)
            .Value;
        inactive.Deactivate(DateTime.UtcNow);

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await db.Users.AddRangeAsync(activeA, activeB, inactive);
            await db.SaveChangesAsync();
        }
        bus.Published.Clear();

        var republished = await RunReconcileAsync(provider.GetRequiredService<IServiceScopeFactory>());

        Assert.Equal(2, republished);
        var events = bus.Published.OfType<UserRolesChangedIntegrationEvent>().ToList();
        Assert.Equal(2, events.Count);
        var userIds = events.Select(e => e.UserId).ToHashSet();
        Assert.Contains(activeA.Id, userIds);
        Assert.Contains(activeB.Id, userIds);
        Assert.DoesNotContain(inactive.Id, userIds);
    }

    [Fact]
    public async Task Is_a_noop_when_there_are_no_active_users()
    {
        var (provider, bus) = BuildProvider(Guid.NewGuid().ToString());

        var republished = await RunReconcileAsync(provider.GetRequiredService<IServiceScopeFactory>());

        Assert.Equal(0, republished);
        Assert.Empty(bus.Published.OfType<UserRolesChangedIntegrationEvent>());
    }

    private static (ServiceProvider Provider, FakeMessageBus Bus) BuildProvider(string databaseName)
    {
        var bus = new FakeMessageBus();
        var tenantContext = new TenantContext();
        var services = new ServiceCollection();
        services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<Wolverine.IMessageBus>(bus);
        services.AddSingleton(tenantContext);
        services.AddSingleton<ITenantContext>(tenantContext);
        services.AddSingleton<IRoleRepository>(new FakeRoleRepository());
        services.AddSingleton<ICorrelationContext>(new FakeCorrelationContext());
        return (services.BuildServiceProvider(), bus);
    }

    private static async Task<int> RunReconcileAsync(IServiceScopeFactory scopeFactory)
    {
        var service = new PermissionsReconciliationService(
            scopeFactory,
            new NoopHostApplicationLifetime(),
            NullLogger<PermissionsReconciliationService>.Instance
        );
        var method = typeof(PermissionsReconciliationService).GetMethod(
            "ReconcileOnceAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return await (Task<int>)method.Invoke(service, [CancellationToken.None])!;
    }

    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        public Task<IReadOnlyList<Role>> GetUserRolesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Role>>([]);

        public Task<IReadOnlyList<string>> GetEffectivePermissionCodesAsync(Guid userId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(["customers.view"]);

        public Task<Role?> GetByIdAsync(Guid roleId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Role>> GetByIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task AddAsync(Role role, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<bool> NameExistsAsync(Guid tenantId, string name, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersInRoleAsync(Guid roleId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Permission>> GetPermissionsCatalogAsync(CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task ReplaceUserRolesAsync(
            Guid userId,
            IReadOnlyCollection<Guid> roleIds,
            Guid? assignedByUserId,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task EnsureSystemRolesAsync(Guid tenantId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<Role?> GetSystemRoleAsync(Guid tenantId, string systemRoleName, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation-id";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoopScope();

        private sealed class NoopScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
