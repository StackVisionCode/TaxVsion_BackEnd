using System.Security.Claims;
using System.Text.Json;
using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Session;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.SlotGating.Abstractions;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.SlotGating.Consumers;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.SlotGating;

/// <summary>
/// Escenarios end-to-end del plan §6: handler + proyección (en memoria) + middleware real. Los
/// bloqueantes requieren SQL/Rabbit; esta variante mocked prueba que la decisión del calculator se
/// propaga al 403 estructurado del middleware sin levantar flota.
/// </summary>
public sealed class SlotGatingIntegrationTests
{
    [Fact]
    public async Task Enforce_returns_structured_403_for_user_outside_cap()
    {
        var world = await IntegrationWorld.NewAsync(cap: 2);
        var owner = world.AddOwner();
        var inside = world.AddEmployee("Ana", "Lead", DateTime.UtcNow.AddDays(-10));
        var outside = world.AddEmployee("Beto", "Junior", DateTime.UtcNow);

        await world.RecomputeAsync();

        // Shadow deja pasar al que estaría gateado (sin tocar status).
        var shadow = await world.InvokeAsync(outside.Id, SlotGatingMode.Shadow);
        Assert.True(shadow.ReachedNext);
        Assert.Equal(StatusCodes.Status200OK, shadow.StatusCode);

        // Enforce lo bloquea con body estructurado.
        var enforced = await world.InvokeAsync(outside.Id, SlotGatingMode.Enforce);
        Assert.False(enforced.ReachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, enforced.StatusCode);
        using var body = JsonDocument.Parse(enforced.Body);
        Assert.Equal("seats.user_slot_gated", body.RootElement.GetProperty("error").GetString());
        Assert.Equal(3, body.RootElement.GetProperty("slotIndex").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("effectiveCap").GetInt32());

        // Owner y el user dentro del cap pasan igual en Enforce.
        Assert.True((await world.InvokeAsync(owner.Id, SlotGatingMode.Enforce)).ReachedNext);
        Assert.True((await world.InvokeAsync(inside.Id, SlotGatingMode.Enforce)).ReachedNext);
    }

    [Fact]
    public async Task Suspending_add_on_seat_moves_last_user_to_gated()
    {
        var world = await IntegrationWorld.NewAsync(cap: 3);
        world.AddOwner();
        world.AddEmployee("Ana", "Lead", DateTime.UtcNow.AddDays(-20));
        var trailing = world.AddEmployee("Beto", "Junior", DateTime.UtcNow.AddDays(-5));

        await world.RecomputeAsync();
        Assert.True((await world.InvokeAsync(trailing.Id, SlotGatingMode.Enforce)).ReachedNext);

        // Un seat add-on se suspende (B5): el cap cae a 2 y el calculator debe gatear al slot 3.
        world.SetCap(2);
        await world.RecomputeAsync();

        var afterSuspend = await world.InvokeAsync(trailing.Id, SlotGatingMode.Enforce);
        Assert.False(afterSuspend.ReachedNext);
        Assert.Equal(StatusCodes.Status403Forbidden, afterSuspend.StatusCode);
    }

    [Fact]
    public async Task Reactivating_add_on_seat_unlocks_user_and_bumps_permissions_version()
    {
        var world = await IntegrationWorld.NewAsync(cap: 2);
        world.AddOwner();
        world.AddEmployee("Ana", "Lead", DateTime.UtcNow.AddDays(-20));
        var trailing = world.AddEmployee("Beto", "Junior", DateTime.UtcNow);

        await world.RecomputeAsync();
        var versionBeforeUnlock = trailing.PermissionsVersion;

        // Reactivación de seat (B5) sube cap y el user vuelve.
        world.SetCap(3);
        await world.RecomputeAsync();

        // El handler emitió UserSlotUnlocked; el consumer bumpa PermissionsVersion.
        var unlocked = Assert.Single(
            world.Bus.Published.OfType<BuildingBlocks.Messaging.AuthIntegrationEvents.UserSlotUnlockedIntegrationEvent>()
        );
        await UserSlotUnlockedConsumer.Handle(unlocked, world.Users, world.UnitOfWork, CancellationToken.None);
        Assert.Equal(versionBeforeUnlock + 1, trailing.PermissionsVersion);

        var afterReactivate = await world.InvokeAsync(trailing.Id, SlotGatingMode.Enforce);
        Assert.True(afterReactivate.ReachedNext);
        Assert.Equal(StatusCodes.Status200OK, afterReactivate.StatusCode);
    }

    [Fact]
    public async Task Admin_override_descends_user_in_queue_and_gates_the_one_bumped_out()
    {
        var world = await IntegrationWorld.NewAsync(cap: 2);
        world.AddOwner();
        var oldSenior = world.AddEmployee("Ana", "Lead", DateTime.UtcNow.AddDays(-30));
        var newcomer = world.AddEmployee("Beto", "Junior", DateTime.UtcNow);

        await world.RecomputeAsync();
        Assert.True((await world.InvokeAsync(oldSenior.Id, SlotGatingMode.Enforce)).ReachedNext);
        Assert.False((await world.InvokeAsync(newcomer.Id, SlotGatingMode.Enforce)).ReachedNext);

        // Override admin: baja a Ana al final de la cola (SlotPriorityAt más reciente que Beto).
        oldSenior.OverrideSlotPriority(newcomer.SlotPriorityAt!.Value.AddTicks(1), Guid.NewGuid(), "demote");
        await world.RecomputeAsync();

        Assert.False((await world.InvokeAsync(oldSenior.Id, SlotGatingMode.Enforce)).ReachedNext);
        Assert.True((await world.InvokeAsync(newcomer.Id, SlotGatingMode.Enforce)).ReachedNext);
    }

    private sealed class IntegrationWorld
    {
        public Guid TenantId { get; }
        public RecomputeTenantSeatSlotsHandlerTests.UsersRepo Users { get; }
        public TenantPlanLimits Limits { get; }
        public ProjectionsRepo Projections { get; }
        public FakeMessageBus Bus { get; }
        public FakeUnitOfWork UnitOfWork { get; }
        private readonly Tenant _tenant;

        private IntegrationWorld(Guid tenantId, int cap)
        {
            TenantId = tenantId;
            Users = new RecomputeTenantSeatSlotsHandlerTests.UsersRepo();
            Limits = TenantPlanLimits.Create(tenantId, "pro", cap, 5, 0, "[]");
            Projections = new ProjectionsRepo();
            Bus = new FakeMessageBus();
            UnitOfWork = new FakeUnitOfWork();
            _tenant = Tenant
                .Register(tenantId, "Acme Tax", "acme", BuildingBlocks.Tenancy.TenantKind.Customer, "Etc/UTC")
                .Value;
        }

        public static Task<IntegrationWorld> NewAsync(int cap) =>
            Task.FromResult(new IntegrationWorld(Guid.NewGuid(), cap));

        public User AddOwner()
        {
            // onboardingId → Register pone SlotPriorityAt = MinValue (IsOwner true).
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

        public User AddEmployee(string name, string lastName, DateTime slotPriorityAt)
        {
            var employee = User.Register(
                TenantId,
                name,
                lastName,
                $"{name.ToLowerInvariant()}@example.com",
                "hash",
                UserActorType.TenantEmployee
            ).Value;
            // Register setea SlotPriorityAt = UtcNow; sobreescribimos para controlar el orden en el test.
            employee.OverrideSlotPriority(slotPriorityAt, Guid.NewGuid(), "test setup");
            Users.Add(employee);
            return employee;
        }

        public void SetCap(int cap) => Limits.SetMaxUsers(cap);

        public Task RecomputeAsync() =>
            RecomputeTenantSeatSlotsHandler.Handle(
                new RecomputeTenantSeatSlotsCommand(TenantId),
                Users,
                new LimitsStore(Limits),
                Projections,
                new Serializer(),
                new TenantsRegistry(_tenant),
                UnitOfWork,
                Bus,
                new FakeCorrelationContext(),
                TimeProvider.System,
                CancellationToken.None
            );

        public async Task<Invocation> InvokeAsync(Guid userId, SlotGatingMode mode)
        {
            var reachedNext = false;
            var middleware = new SeatSlotGateMiddleware(
                _ =>
                {
                    reachedNext = true;
                    return Task.CompletedTask;
                },
                new StaticOptionsMonitor(new SlotGatingOptions { Mode = mode }),
                new TestHostEnvironment(),
                NullLogger<SeatSlotGateMiddleware>.Instance
            );

            var context = new DefaultHttpContext();
            context.Request.Scheme = "https";
            context.Request.Host = new HostString("office.taxproffice.com");
            context.Request.Path = "/customers";
            context.Response.Body = new MemoryStream();
            var claims = new List<Claim>
            {
                new("sub", userId.ToString()),
                new(ClaimNames.TenantId, TenantId.ToString()),
                new(ClaimNames.ActorType, ActorType.TenantEmployee.ToString()),
            };
            context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

            await middleware.InvokeAsync(context, new ProjectionGate(TenantId, Projections));
            context.Response.Body.Position = 0;
            var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
            return new Invocation(reachedNext, context.Response.StatusCode, body);
        }

        public sealed record Invocation(bool ReachedNext, int StatusCode, string Body);
    }

    private sealed class ProjectionGate(Guid tenantId, ProjectionsRepo projections) : ISeatSlotGate
    {
        public ValueTask<SeatSlotDecision> CheckAsync(Guid _, Guid userId, CancellationToken __)
        {
            var row = projections.Rows.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId);
            if (row is null)
                return ValueTask.FromResult(new SeatSlotDecision(true, null, null, null));
            return ValueTask.FromResult(
                new SeatSlotDecision(
                    Allowed: !row.IsGated,
                    Reason: row.IsGated ? "seats.user_slot_gated" : null,
                    SlotIndex: row.SlotIndex,
                    EffectiveCap: row.EffectiveCap
                )
            );
        }
    }

    private sealed class ProjectionsRepo : ITenantSeatSlotProjectionRepository
    {
        public List<TenantSeatSlotProjectionRecord> Rows { get; } = [];

        public Task<IReadOnlyList<TenantSeatSlotProjectionRecord>> GetByTenantAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<TenantSeatSlotProjectionRecord>>([
                .. Rows.Where(r => r.TenantId == tenantId),
            ]);

        public Task<TenantSeatSlotProjectionRecord?> GetAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId));

        public Task ReplaceAsync(
            Guid tenantId,
            IReadOnlyList<TenantSeatSlotProjectionRecord> projections,
            CancellationToken ct = default
        )
        {
            Rows.RemoveAll(r => r.TenantId == tenantId);
            Rows.AddRange(projections);
            return Task.CompletedTask;
        }
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

    private sealed class Serializer : ISeatSlotRecomputeSerializer
    {
        public Task<IAsyncDisposable> AcquireAsync(Guid tenantId, CancellationToken ct) =>
            Task.FromResult<IAsyncDisposable>(new Release());

        private sealed class Release : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class StaticOptionsMonitor(SlotGatingOptions value) : IOptionsMonitor<SlotGatingOptions>
    {
        public SlotGatingOptions CurrentValue => value;

        public SlotGatingOptions Get(string? name) => value;

        public IDisposable? OnChange(Action<SlotGatingOptions, string?> listener) => null;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "TaxVision.Auth.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
