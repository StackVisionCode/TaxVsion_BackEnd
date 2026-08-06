using System.Reflection;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Auth.Api.Bootstrap;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Infrastructure.Persistence;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Infrastructure;

/// <summary>
/// RBAC Fase 2 (RBAC_Hardening_Plan.md) — cubre el caso que el plan pedía para un
/// <c>TenantAdminPermissionsBackfillService</c> nuevo, pero contra <see cref="SystemRolePermissionsSyncService"/>
/// (ver comentario de esa clase: se reusó en vez de duplicar). <c>ExecuteAsync</c> es <c>protected</c>
/// (la clase es <c>sealed</c>, no se puede subclasear para exponerlo) — se invoca por reflection,
/// mismo patrón que ya usan otros hosted services de arranque sin WebApplicationFactory en el repo.
/// </summary>
public sealed class SystemRolePermissionsSyncServiceTests
{
    private sealed class NoopHostApplicationLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => CancellationToken.None;
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    /// <summary>RBAC Fase 5 — sin tenant a propósito: la query del servicio bajo prueba ya usa IgnoreQueryFilters().</summary>
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static (ServiceProvider Provider, FakeMessageBus Bus) BuildProvider(string databaseName)
    {
        var bus = new FakeMessageBus();
        var services = new ServiceCollection();
        services.AddDbContext<AuthDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddSingleton<Wolverine.IMessageBus>(bus);
        services.AddSingleton<ITenantContext>(new NoTenantContext());
        return (services.BuildServiceProvider(), bus);
    }

    private static Task RunSyncAsync(IServiceScopeFactory scopeFactory)
    {
        var service = new SystemRolePermissionsSyncService(
            scopeFactory,
            new NoopHostApplicationLifetime(),
            NullLogger<SystemRolePermissionsSyncService>.Instance
        );
        var method = typeof(SystemRolePermissionsSyncService).GetMethod(
            "ExecuteAsync",
            BindingFlags.NonPublic | BindingFlags.Instance
        )!;
        return (Task)method.Invoke(service, [CancellationToken.None])!;
    }

    [Fact]
    public async Task Resync_keeps_dangerous_permissions_on_an_existing_TenantAdmin_role_and_bumps_version()
    {
        var (provider, bus) = BuildProvider(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();
        Guid roleId;

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
            // Estado "viejo": el rol solo tiene roles.manage, le faltan el resto de los permisos
            // del catalogo (ej. customers.view) — el resync debe completarlo.
            role.SetPermissions([PermissionCatalog.IdOf(PermissionCatalog.RolesManage)], seeding: true);
            roleId = role.Id;

            await db.Roles.AddAsync(role);
            await db.SaveChangesAsync();
        }

        await RunSyncAsync(provider.GetRequiredService<IServiceScopeFactory>());

        await using (var verifyScope = provider.CreateAsyncScope())
        {
            var db = verifyScope.ServiceProvider.GetRequiredService<AuthDbContext>();
            // RBAC Fase 5 — verificación de test sin tenant en contexto (ver NoTenantContext).
            var role = await db.Roles.IgnoreQueryFilters().Include(r => r.Permissions).SingleAsync(r => r.Id == roleId);

            var currentIds = role.Permissions.Select(link => link.PermissionId).ToHashSet();
            // 2026-08-06: el rol de sistema TenantAdmin usa el set que SI incluye IsDangerous — ver
            // doc-comment de PermissionCatalog.SystemTenantAdminRootPermissions.
            var expectedIds = PermissionCatalog
                .SystemTenantAdminRootPermissions()
                .Select(PermissionCatalog.IdOf)
                .ToHashSet();

            Assert.Equal(expectedIds, currentIds);
            Assert.Contains(PermissionCatalog.IdOf(PermissionCatalog.RolesManage), currentIds);
            Assert.True(role.PermissionsVersion > 0);
        }

        var published = Assert.Single(bus.Published.OfType<RolePermissionsChangedIntegrationEvent>());
        Assert.Equal(roleId, published.RoleId);
        Assert.Equal(tenantId, published.TenantId);
        Assert.Contains(PermissionCatalog.RolesManage, published.PermissionCodes);
        Assert.Contains(PermissionCatalog.CustomersView, published.PermissionCodes);
    }

    [Fact]
    public async Task Resync_is_a_noop_when_role_permissions_already_match_the_catalog()
    {
        var (provider, bus) = BuildProvider(Guid.NewGuid().ToString());
        var tenantId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var role = Role.Create(tenantId, Role.SystemTenantAdmin, null, isSystem: true).Value;
            var expectedIds = PermissionCatalog
                .SystemTenantAdminRootPermissions()
                .Select(PermissionCatalog.IdOf)
                .ToList();
            role.SetPermissions(expectedIds, seeding: true);

            await db.Roles.AddAsync(role);
            await db.SaveChangesAsync();
        }
        bus.Published.Clear();

        await RunSyncAsync(provider.GetRequiredService<IServiceScopeFactory>());

        Assert.Empty(bus.Published);
    }
}
