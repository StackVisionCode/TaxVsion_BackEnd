using BuildingBlocks.Authorization;
using BuildingBlocks.CustomerVisibility;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.RateLimiting.Abstractions;
using TaxVision.Notification.Domain.Permissions;
using TaxVision.Notification.Domain.RateLimiting;
using TaxVision.Notification.Domain.Recipients;
using TaxVision.Notification.Infrastructure.Permissions;

namespace TaxVision.Notification.Tests;

/// <summary>T4.2 — validación al generar: activo + capacidad + módulo del tenant + excluir al actor.</summary>
public sealed class RecipientResolverValidationTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserA = Guid.NewGuid();
    private static readonly Guid UserB = Guid.NewGuid();

    [Fact]
    public async Task Participants_filtra_por_activo_y_capacidad()
    {
        var permissions = new FakePermissions().WithActive(UserA, "billing.view").WithActive(UserB); // activo pero sin la capacidad
        var resolver = CreateResolver(permissions, modules: null);

        var result = await resolver.ResolveAsync(
            new Participants(TenantId, [UserA, UserB]),
            new RecipientValidation(RequiredCapability: "billing.view")
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([UserA], result.Value);
    }

    [Fact]
    public async Task Excluye_al_actor_del_hecho()
    {
        var permissions = new FakePermissions().WithActive(UserA).WithActive(UserB);
        var resolver = CreateResolver(permissions, modules: null);

        var result = await resolver.ResolveAsync(
            new Participants(TenantId, [UserA, UserB]),
            new RecipientValidation(ExcludeActorUserId: UserA)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([UserB], result.Value);
    }

    [Fact]
    public async Task Modulo_deshabilitado_en_el_tenant_bloquea()
    {
        var permissions = new FakePermissions().WithActive(UserA);
        var resolver = CreateResolver(permissions, modules: ["email"]);

        var result = await resolver.ResolveAsync(
            new Participants(TenantId, [UserA]),
            new RecipientValidation(RequiredModule: "documents")
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Notification.Audience.ModuleDisabled", result.Error.Code);
    }

    [Fact]
    public async Task Sin_fila_de_plan_el_modulo_no_bloquea_fail_open()
    {
        var permissions = new FakePermissions().WithActive(UserA);
        var resolver = CreateResolver(permissions, modules: null);

        var result = await resolver.ResolveAsync(
            new Participants(TenantId, [UserA]),
            new RecipientValidation(RequiredModule: "documents")
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([UserA], result.Value);
    }

    [Fact]
    public async Task CustomerPortal_sin_proyeccion_devuelve_fallo_explicito()
    {
        var resolver = CreateResolver(new FakePermissions(), modules: null);

        var result = await resolver.ResolveAsync(
            new CustomerPortal(TenantId, Guid.NewGuid()),
            RecipientValidation.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Notification.Audience.NoPortalAccount", result.Error.Code);
    }

    [Fact]
    public async Task Mailbox_de_oficina_sin_asignado_usa_admin_con_office_read()
    {
        var permissions = new FakePermissions()
            .WithActive(UserA, ConnectorsPermissions.AccountsOfficeRead)
            .WithActive(UserB, ConnectorsPermissions.AccountsOfficeRead);
        var resolver = new RecipientResolver(
            permissions,
            new FakeAssignments(),
            new FakePlans(null),
            new FakeOwners(UserA),
            new FakePortalAccounts()
        );

        var result = await resolver.ResolveAsync(
            new MailboxAudience(TenantId, Guid.NewGuid(), null, Guid.NewGuid()),
            RecipientValidation.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([UserA], result.Value);
    }

    [Fact]
    public async Task Mailbox_de_oficina_une_preparador_asignado_con_admins_office_read()
    {
        // Política B — admin dueño siempre sabe del buzón de oficina aunque haya preparador asignado.
        var permissions = new FakePermissions()
            .WithActive(UserA, ConnectorsPermissions.AccountsOfficeRead)
            .WithActive(UserB, ConnectorsPermissions.AccountsOfficeRead);
        var resolver = new RecipientResolver(
            permissions,
            new FakeAssignments().WithAssigned(UserB),
            new FakePlans(null),
            new FakeOwners(UserA),
            new FakePortalAccounts()
        );

        var result = await resolver.ResolveAsync(
            new MailboxAudience(TenantId, Guid.NewGuid(), null, Guid.NewGuid()),
            RecipientValidation.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal([UserB, UserA], result.Value);
    }

    private static RecipientResolver CreateResolver(FakePermissions permissions, IReadOnlyList<string>? modules) =>
        new(permissions, new FakeAssignments(), new FakePlans(modules), new FakeOwners(), new FakePortalAccounts());

    private sealed class FakePermissions : INotificationRecipientPermissionsProjectionRepository
    {
        private readonly Dictionary<Guid, List<string>> _active = [];

        public FakePermissions WithActive(Guid userId, params string[] capabilities)
        {
            _active[userId] = [.. capabilities];
            return this;
        }

        public Task<IReadOnlyList<Guid>> FilterActiveWithCapabilityAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> userIds,
            string? requiredCapability,
            CancellationToken ct = default
        )
        {
            var matches = userIds
                .Where(id => _active.ContainsKey(id))
                .Where(id => requiredCapability is null || _active[id].Contains(requiredCapability))
                .ToList();
            return Task.FromResult<IReadOnlyList<Guid>>(matches);
        }

        public Task<IReadOnlyList<Guid>> FindActiveUserIdsByPermissionAsync(
            Guid tenantId,
            string permissionCode,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<Guid>>(
                _active.Where(kv => kv.Value.Contains(permissionCode)).Select(kv => kv.Key).ToList()
            );

        public Task<NotificationRecipientPermissionsProjection?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationRecipientPermissionsProjection?>(null);

        public Task AddAsync(NotificationRecipientPermissionsProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<NotificationRecipientPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
            Guid tenantId,
            Guid roleId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<NotificationRecipientPermissionsProjection>>([]);
    }

    private sealed class FakeAssignments : ICustomerAssignmentProjectionStore
    {
        private IReadOnlyCollection<Guid> _assigned = [];

        public FakeAssignments WithAssigned(params Guid[] userIds)
        {
            _assigned = userIds;
            return this;
        }

        public Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult<DateTime?>(null);

        public Task<bool> IsAnyAssignedToUserAsync(
            Guid tenantId,
            Guid userId,
            IReadOnlyCollection<Guid> customerIds,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyCollection<Guid>>([]);

        public Task<IReadOnlyCollection<Guid>> GetAssignedUserIdsAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult(_assigned);

        public Task ReplaceAsync(
            Guid tenantId,
            Guid customerId,
            IReadOnlyCollection<Guid> userIds,
            DateTime version,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class FakePlans(IReadOnlyList<string>? modules) : ITenantPlanCodeProjectionRepository
    {
        public Task<TenantPlanCodeProjection?> GetAsync(Guid tenantId, CancellationToken ct = default)
        {
            if (modules is null)
                return Task.FromResult<TenantPlanCodeProjection?>(null);
            var projection = TenantPlanCodeProjection.Create(tenantId, "pro", 1);
            projection.ApplyIfNewer("pro", 1, modules);
            return Task.FromResult<TenantPlanCodeProjection?>(projection);
        }

        public Task AddAsync(TenantPlanCodeProjection projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakeOwners : ITenantOwnerProjectionRepository
    {
        private readonly IReadOnlyList<Guid> _activeAdmins;

        public FakeOwners(params Guid[] activeAdmins)
        {
            _activeAdmins = activeAdmins;
        }

        public Task<Guid?> GetActiveOwnerUserIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task<IReadOnlyList<Guid>> GetActiveAdminUserIdsAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_activeAdmins);

        public Task<TenantOwnerProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult<TenantOwnerProjection?>(null);

        public Task<TenantOwnerProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<TenantOwnerProjection?>(null);

        public Task AddAsync(TenantOwnerProjection projection, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FakePortalAccounts : ICustomerPortalAccountProjectionRepository
    {
        public Task<Guid?> GetActivePortalUserIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            Task.FromResult<Guid?>(null);

        public Task<CustomerPortalAccountProjection?> GetByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task<CustomerPortalAccountProjection?> GetByUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerPortalAccountProjection?>(null);

        public Task AddAsync(CustomerPortalAccountProjection projection, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
