using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.SlotGating.Consumers;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.SlotGating;

public sealed class RecomputeTenantSeatSlotsHandlerTests
{
    [Fact]
    public async Task Recompute_emits_gated_event_when_user_falls_outside_cap()
    {
        var world = SeatSlotWorld.New(cap: 1);
        var owner = world.AddOwner();
        var employee = world.AddEmployee();

        await world.HandleAsync();

        var gated = Assert.Single(world.Bus.Published.OfType<UserSlotGatedIntegrationEvent>());
        Assert.Equal(employee.Id, gated.UserId);
        Assert.Equal(2, gated.SlotIndex);
        Assert.Equal(1, gated.EffectiveCap);
        Assert.Equal("Ana Staff", gated.UserFullName);
        Assert.Equal("staff@example.com", gated.UserEmail);
        Assert.Equal("owner@example.com", gated.PrimaryAdminEmail);
        Assert.Equal("Acme Tax", gated.TenantName);
        Assert.Equal(1, world.Projections.Rows[0].RevisionNumber);
        Assert.True(world.Projections.Rows.Single(row => row.UserId == employee.Id).IsGated);
        Assert.False(world.Projections.Rows.Single(row => row.UserId == owner.Id).IsGated);
    }

    [Fact]
    public async Task Recompute_unlock_emits_event_and_consumer_bumps_permissions_version()
    {
        var world = SeatSlotWorld.New(cap: 1);
        var owner = world.AddOwner();
        var employee = world.AddEmployee();

        // Estado inicial: employee en slot 2, gated.
        await world.HandleAsync();
        world.Bus.Published.Clear();

        // El cap sube: la segunda invocación debería desbloquear al employee.
        world.Limits.SetMaxUsers(2);
        await world.HandleAsync();

        var unlocked = Assert.Single(world.Bus.Published.OfType<UserSlotUnlockedIntegrationEvent>());
        Assert.Equal(employee.Id, unlocked.UserId);
        Assert.Equal("staff@example.com", unlocked.UserEmail);
        Assert.False(world.Projections.Rows.Single(row => row.UserId == employee.Id).IsGated);

        // El consumer de UserSlotUnlocked bumpa PermissionsVersion para que /auth/me/access
        // deje de responder 304 — es parte del contrato de cierre descrito en el plan §3.3.
        var employeeVersionBefore = employee.PermissionsVersion;
        var ownerVersionBefore = owner.PermissionsVersion;
        await UserSlotUnlockedConsumer.Handle(unlocked, world.Users, world.UnitOfWork, CancellationToken.None);

        Assert.Equal(employeeVersionBefore + 1, employee.PermissionsVersion);
        Assert.Equal(ownerVersionBefore, owner.PermissionsVersion);
    }

    [Fact]
    public async Task Recompute_is_idempotent_when_state_did_not_change()
    {
        var world = SeatSlotWorld.New(cap: 1);
        world.AddOwner();
        world.AddEmployee();

        await world.HandleAsync();
        world.Bus.Published.Clear();
        var saveCountAfterFirst = world.UnitOfWork.SaveChangesCallCount;
        var revisionAfterFirst = world.Projections.Rows[0].RevisionNumber;

        await world.HandleAsync();
        await world.HandleAsync();

        Assert.Empty(world.Bus.Published);
        Assert.Equal(saveCountAfterFirst, world.UnitOfWork.SaveChangesCallCount);
        Assert.Equal(revisionAfterFirst, world.Projections.Rows[0].RevisionNumber);
    }

    [Fact]
    public async Task Recompute_with_refresh_flag_bumps_revision_without_emitting()
    {
        var world = SeatSlotWorld.New(cap: 1);
        world.AddOwner();
        world.AddEmployee();

        await world.HandleAsync();
        world.Bus.Published.Clear();
        var revisionAfterFirst = world.Projections.Rows[0].RevisionNumber;

        await world.HandleAsync(refreshUnchanged: true);

        Assert.Empty(world.Bus.Published);
        Assert.Equal(revisionAfterFirst + 1, world.Projections.Rows[0].RevisionNumber);
    }

    private sealed class SeatSlotWorld
    {
        public Guid TenantId { get; }
        public UsersRepo Users { get; }
        public TenantPlanLimits Limits { get; }
        public Projections Projections { get; }
        public FakeMessageBus Bus { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        public Tenant Tenant { get; }

        private SeatSlotWorld(Guid tenantId, int cap)
        {
            TenantId = tenantId;
            Users = new UsersRepo();
            Limits = TenantPlanLimits.Create(tenantId, "pro", cap, 5, 0, "[]");
            Projections = new Projections();
            Bus = new FakeMessageBus();
            UnitOfWork = new FakeUnitOfWork();
            Tenant = Tenant
                .Register(tenantId, "Acme Tax", "acme", BuildingBlocks.Tenancy.TenantKind.Customer, "Etc/UTC")
                .Value;
        }

        public static SeatSlotWorld New(int cap) => new(Guid.NewGuid(), cap);

        public User AddOwner()
        {
            var owner = User.Register(
                TenantId,
                "Ada",
                "Owner",
                "owner@example.com",
                "hash",
                UserActorType.TenantAdmin,
                onboardingId: Guid.NewGuid()
            ).Value;
            Users.Add(owner);
            return owner;
        }

        public User AddEmployee(string name = "Ana", string lastName = "Staff", string? email = null)
        {
            var employee = User.Register(
                TenantId,
                name,
                lastName,
                email ?? "staff@example.com",
                "hash",
                UserActorType.TenantEmployee
            ).Value;
            Users.Add(employee);
            return employee;
        }

        public Task HandleAsync(bool refreshUnchanged = false) =>
            RecomputeTenantSeatSlotsHandler.Handle(
                new RecomputeTenantSeatSlotsCommand(TenantId, refreshUnchanged),
                Users,
                new LimitsStore(Limits),
                Projections,
                new Serializer(),
                new TenantsRegistry(Tenant),
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                TimeProvider.System,
                CancellationToken.None
            );
    }

    private sealed class TenantsRegistry(Tenant tenant) : ITenantRegistry
    {
        public Task<Tenant?> GetByIdAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<Tenant?>(tenant);

        public Task UpsertCreatedAsync(
            Guid tenantId,
            string name,
            string subDomain,
            BuildingBlocks.Tenancy.TenantKind kind,
            string defaultTimeZoneId,
            CancellationToken ct = default
        ) => Task.CompletedTask;

        public Task SetActiveAsync(Guid tenantId, bool isActive, CancellationToken ct = default) => Task.CompletedTask;

        public Task SetBillingBlockedAsync(
            Guid tenantId,
            bool blocked,
            string? reason,
            CancellationToken ct = default
        ) => Task.CompletedTask;
    }

    private sealed class LimitsStore(TenantPlanLimits value) : ITenantPlanLimitsStore
    {
        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantPlanLimits?>(value);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Projections : ITenantSeatSlotProjectionRepository
    {
        public IReadOnlyList<TenantSeatSlotProjectionRecord> Rows { get; private set; } = [];

        public Task<IReadOnlyList<TenantSeatSlotProjectionRecord>> GetByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows);

        public Task<TenantSeatSlotProjectionRecord?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(row => row.UserId == userId));

        public Task ReplaceAsync(
            Guid tenantId,
            IReadOnlyList<TenantSeatSlotProjectionRecord> projections,
            CancellationToken ct = default
        )
        {
            Rows = projections;
            return Task.CompletedTask;
        }
    }

    private sealed class Serializer : ISeatSlotRecomputeSerializer
    {
        public Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable>(new Release());

        private sealed class Release : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    internal sealed class UsersRepo : IUserRepository
    {
        private readonly List<User> _values = [];

        public void Add(User user) => _values.Add(user);

        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_values.FirstOrDefault(user => user.Id == id));

        public Task<User?> GetByEmailAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult<User?>(null);

        public Task<bool> EmailExistsAsync(
            Guid tenantId,
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult(false);

        public Task<User?> GetPortalUserByCustomerAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<User?>(null);

        public Task<User?> GetByOnboardingIdAsync(Guid onboardingId, CancellationToken ct = default) =>
            Task.FromResult<User?>(null);

        public Task<IReadOnlyList<Guid>> GetActiveTenantIdsByEmailAsync(
            string email,
            UserAccountKind kind,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task AddAsync(User user, CancellationToken ct = default)
        {
            _values.Add(user);
            return Task.CompletedTask;
        }

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_values.Count);

        public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<User>>(_values);

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(_values.FirstOrDefault(user => user.ActorType == UserActorType.TenantAdmin));

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => Task.FromResult(((IReadOnlyList<User>)_values, _values.Count));
    }
}
