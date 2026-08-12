using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Application.Permissions.Consumers;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Tests.Permissions;

/// <summary>
/// Checkpoint 3 de <c>03_Plan_De_Fases.md</c> — upsert idempotente por versión y unión de permisos
/// de un usuario multi-rol. Fakes locales de mano, sin Moq: se llama <c>Handle(...)</c> directo.
///
/// <para>
/// Los dos casos que existen por bugs reales ya vistos: el upsert de Communication no comparaba
/// <c>PermVersion</c> y dejaba usuarios fail-closed en silencio sin DLQ, y sobreescribir con los
/// códigos del rol que cambió le borra a un usuario multi-rol los permisos de sus otros roles.
/// </para>
/// </summary>
public sealed class PermissionsProjectionConsumersTests
{
    [Fact]
    public async Task UserRolesChanged_creates_projection_when_none_exists()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var repo = new RecordingUserRepository();
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 1,
            PermissionCodes = [ReminderPermissions.Read],
            RoleIds = [roleId],
            ActorType = "TenantEmployee",
        };

        await UserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<UserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.NotNull(stored);
        Assert.Equal(1, stored!.PermissionsVersion);
        Assert.Equal([ReminderPermissions.Read], stored.PermissionCodes());
        Assert.Equal([roleId], stored.RoleIds());
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task UserRolesChanged_applies_newer_version_to_existing_projection()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = UserPermissionsProjection.Create(tenantId, userId, 1, [ReminderPermissions.Read], []);
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 2,
            PermissionCodes = [ReminderPermissions.Read, ReminderPermissions.Write],
            RoleIds = [],
            ActorType = "TenantEmployee",
        };

        await UserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<UserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.Equal(2, stored!.PermissionsVersion);
        Assert.Equal([ReminderPermissions.Read, ReminderPermissions.Write], stored.PermissionCodes());
    }

    /// <summary>
    /// El caso que el plan pide explícitamente: un evento con PermVersion vieja NO pisa la fila.
    /// Sin esto, un redelivery fuera de orden le quita permisos a un usuario en silencio.
    /// </summary>
    [Fact]
    public async Task UserRolesChanged_ignores_out_of_order_event_with_older_permissions_version()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var existing = UserPermissionsProjection.Create(
            tenantId,
            userId,
            5,
            [ReminderPermissions.Read, ReminderPermissions.Write],
            []
        );
        var repo = new RecordingUserRepository(existing);
        var uow = new NoOpUnitOfWork();

        var evt = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenantId,
            UserId = userId,
            PermissionsVersion = 4,
            PermissionCodes = [],
            RoleIds = [],
            ActorType = "TenantEmployee",
        };

        await UserRolesChangedPermissionsProjectionConsumer.Handle(
            evt,
            repo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<UserPermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await repo.GetAsync(tenantId, userId);
        Assert.Equal(5, stored!.PermissionsVersion);
        Assert.Equal([ReminderPermissions.Read, ReminderPermissions.Write], stored.PermissionCodes());
    }

    [Fact]
    public async Task RolePermissionsChanged_recomputes_union_for_multi_role_affected_user()
    {
        var tenantId = Guid.NewGuid();
        var changedRoleId = Guid.NewGuid();
        var otherRoleId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // El usuario ya tenía otro rol cacheado con sus propios permisos.
        var otherRole = RolePermissionsProjection.Create(
            tenantId,
            otherRoleId,
            "Viewer",
            1,
            [ReminderPermissions.Read]
        );
        var roleRepo = new RecordingRoleRepository(otherRole);

        var user = UserPermissionsProjection.Create(
            tenantId,
            userId,
            1,
            [ReminderPermissions.Read],
            [changedRoleId, otherRoleId]
        );
        var userRepo = new RecordingUserRepository(user);
        var uow = new NoOpUnitOfWork();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = changedRoleId,
            RoleName = "Editor",
            PermissionsVersion = 1,
            PermissionCodes = [ReminderPermissions.Write],
        };

        await RolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepo,
            userRepo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<RolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var storedRole = await roleRepo.GetAsync(tenantId, changedRoleId);
        Assert.NotNull(storedRole);
        Assert.Equal([ReminderPermissions.Write], storedRole!.PermissionCodes());

        // El usuario multi-rol conserva AMBOS conjuntos, no solo el del rol que cambió.
        var storedUser = await userRepo.GetAsync(tenantId, userId);
        Assert.NotNull(storedUser);
        Assert.Contains(ReminderPermissions.Write, storedUser!.PermissionCodes());
        Assert.Contains(ReminderPermissions.Read, storedUser.PermissionCodes());
        Assert.Equal(1, storedUser.PermissionsVersion); // RolePermissionsChanged no toca la versión del usuario
    }

    [Fact]
    public async Task RolePermissionsChanged_with_no_affected_users_still_saves_the_role_projection()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var roleRepo = new RecordingRoleRepository();
        var userRepo = new RecordingUserRepository();
        var uow = new NoOpUnitOfWork();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = roleId,
            RoleName = "Empty",
            PermissionsVersion = 1,
            PermissionCodes = [ReminderPermissions.Read],
        };

        await RolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepo,
            userRepo,
            uow,
            new NoOpCorrelationContext(),
            NullLogger<RolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        Assert.NotNull(await roleRepo.GetAsync(tenantId, roleId));
        Assert.Equal(1, uow.SaveCount);
    }

    [Fact]
    public async Task RolePermissionsChanged_ignores_out_of_order_event_for_the_role_cache()
    {
        var tenantId = Guid.NewGuid();
        var roleId = Guid.NewGuid();
        var existing = RolePermissionsProjection.Create(
            tenantId,
            roleId,
            "Editor",
            5,
            [ReminderPermissions.Read, ReminderPermissions.Write]
        );
        var roleRepo = new RecordingRoleRepository(existing);
        var uow = new NoOpUnitOfWork();

        var evt = new RolePermissionsChangedIntegrationEvent
        {
            TenantId = tenantId,
            RoleId = roleId,
            RoleName = "Editor",
            PermissionsVersion = 4,
            PermissionCodes = [],
        };

        await RolePermissionsChangedPermissionsProjectionConsumer.Handle(
            evt,
            roleRepo,
            new RecordingUserRepository(),
            uow,
            new NoOpCorrelationContext(),
            NullLogger<RolePermissionsProjection>.Instance,
            CancellationToken.None
        );

        var stored = await roleRepo.GetAsync(tenantId, roleId);
        Assert.Equal(5, stored!.PermissionsVersion);
        Assert.Equal([ReminderPermissions.Read, ReminderPermissions.Write], stored.PermissionCodes());
    }

    // ------------------------------------------------------------------
    // Fakes
    // ------------------------------------------------------------------

    private sealed class RecordingUserRepository(params UserPermissionsProjection[] seed)
        : IUserPermissionsProjectionRepository
    {
        private readonly Dictionary<(Guid TenantId, Guid UserId), UserPermissionsProjection> _byKey = seed.ToDictionary(
            p => (p.TenantId, p.UserId)
        );

        public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(_byKey.GetValueOrDefault((tenantId, userId)));

        public Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default)
        {
            _byKey[(projection.TenantId, projection.UserId)] = projection;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<UserPermissionsProjection>>(
                _byKey.Values.Where(p => p.TenantId == tenantId && p.IsActive && p.RoleIds().Contains(roleId)).ToList()
            );
    }

    private sealed class RecordingRoleRepository(params RolePermissionsProjection[] seed)
        : IRolePermissionsProjectionRepository
    {
        private readonly Dictionary<Guid, RolePermissionsProjection> _byId = seed.ToDictionary(p => p.Id);

        public Task<RolePermissionsProjection?> GetAsync(Guid tenantId, Guid roleId, CancellationToken ct = default) =>
            Task.FromResult(_byId.TryGetValue(roleId, out var role) && role.TenantId == tenantId ? role : null);

        public Task AddAsync(RolePermissionsProjection projection, CancellationToken ct = default)
        {
            _byId[projection.Id] = projection;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<RolePermissionsProjection>> FindByRoleIdsAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> roleIds,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<RolePermissionsProjection>>(
                _byId.Values.Where(r => r.TenantId == tenantId && roleIds.Contains(r.Id)).ToList()
            );
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
