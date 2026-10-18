using BuildingBlocks.Authorization.SlotGating;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.SlotGating.Commands;
using TaxVision.Auth.Application.SlotGating.Queries;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Tenants;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Tests.SlotGating;

public sealed class UserSlotPriorityAdminTests
{
    [Fact]
    public async Task Override_rejects_cross_tenant_target_with_forbidden()
    {
        var target = Staff(Guid.NewGuid(), "Other", "Tenant");
        var result = await HandleOverride(Guid.NewGuid(), target, new Audit());

        Assert.True(result.IsFailure);
        Assert.Equal("SeatSlots.CrossTenant", result.Error.Code);
        Assert.Equal(StatusCodes.Status403Forbidden, result.Error.ToHttpStatusCode());
    }

    [Fact]
    public async Task Override_rejects_portal_user()
    {
        var tenantId = Guid.NewGuid();
        var portal = User.Register(
            tenantId,
            "Client",
            "Portal",
            "portal@example.com",
            "hash",
            UserActorType.CustomerPortal,
            customerId: Guid.NewGuid()
        ).Value;

        var result = await HandleOverride(tenantId, portal, new Audit());

        Assert.True(result.IsFailure);
        Assert.Equal("User.PortalSlot", result.Error.Code);
    }

    [Fact]
    public async Task Override_persists_reason_in_audit_log()
    {
        var tenantId = Guid.NewGuid();
        var target = Staff(tenantId, "Ana", "Lead");
        var audit = new Audit();
        var newPriority = DateTime.UtcNow.AddDays(-5);

        var result = await HandleOverride(tenantId, target, audit, newPriority, "Senior tax lead");

        Assert.True(result.IsSuccess);
        Assert.Equal(newPriority, target.SlotPriorityAt);
        var entry = Assert.Single(audit.Entries);
        Assert.Equal(AuthAuditAction.UserSlotPriorityOverridden, entry.Action);
        Assert.Contains("Senior tax lead", entry.DetailsJson);
    }

    [Fact]
    public async Task Report_orders_owner_first_and_marks_users_beyond_cap_gated()
    {
        var tenantId = Guid.NewGuid();
        var owner = User.Register(
            tenantId,
            "Ada",
            "Owner",
            "owner@example.com",
            "hash",
            UserActorType.TenantAdmin,
            onboardingId: Guid.NewGuid()
        ).Value;
        var employee = Staff(tenantId, "Ana", "Staff");
        var result = await GetTenantSeatSlotsHandler.Handle(
            new GetTenantSeatSlotsQuery(tenantId),
            new Users([employee, owner]),
            new Limits(TenantPlanLimits.Create(tenantId, "starter", 1, 5, 0, "[]")),
            new SlotOptions(SlotGatingMode.Shadow),
            TimeProvider.System,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, result.Value.EffectiveCap);
        Assert.Equal("Shadow", result.Value.Mode);
        Assert.Collection(
            result.Value.Slots,
            slot =>
            {
                Assert.Equal(owner.Id, slot.UserId);
                Assert.True(slot.IsOwner);
                Assert.False(slot.IsGated);
            },
            slot =>
            {
                Assert.Equal(employee.Id, slot.UserId);
                Assert.False(slot.IsOwner);
                Assert.True(slot.IsGated);
            }
        );
    }

    private static Task<BuildingBlocks.Results.Result> HandleOverride(
        Guid tenantId,
        User target,
        Audit audit,
        DateTime? priority = null,
        string reason = "Reorder"
    ) =>
        OverrideUserSlotPriorityHandler.Handle(
            new OverrideUserSlotPriorityCommand(
                tenantId,
                target.Id,
                Guid.NewGuid(),
                UserActorType.TenantAdmin,
                priority ?? DateTime.UtcNow.AddHours(-1),
                reason
            ),
            new Users([target]),
            audit,
            new RequestContext(),
            new CorrelationContext(),
            new UnitOfWork(),
            CancellationToken.None
        );

    private static User Staff(Guid tenantId, string name, string lastName) =>
        User.Register(
            tenantId,
            name,
            lastName,
            $"{name.ToLowerInvariant()}@example.com",
            "hash",
            UserActorType.TenantEmployee
        ).Value;

    private sealed class Users(IReadOnlyList<User> values) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(values.FirstOrDefault(user => user.Id == id));

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

        public Task AddAsync(User user, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(values.Count);

        public Task<IReadOnlyList<User>> GetActiveStaffForSlotGatingAsync(
            Guid tenantId,
            CancellationToken ct = default
        ) => Task.FromResult(values);

        public Task<User?> GetPrimaryAdminAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(values.FirstOrDefault(user => user.ActorType == UserActorType.TenantAdmin));

        public Task<(IReadOnlyList<User> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            int page,
            int size,
            string? search,
            bool? isActive,
            Guid? customerId = null,
            UserAccountKind? accountKind = null,
            CancellationToken ct = default
        ) => Task.FromResult((values, values.Count));
    }

    private sealed class Limits(TenantPlanLimits value) : ITenantPlanLimitsStore
    {
        public Task<TenantPlanLimits?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<TenantPlanLimits?>(value);

        public Task AddAsync(TenantPlanLimits limits, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class SlotOptions(SlotGatingMode mode) : IOptionsMonitor<SlotGatingOptions>
    {
        public SlotGatingOptions CurrentValue { get; } = new() { Mode = mode };

        public SlotGatingOptions Get(string? name) => CurrentValue;

        public IDisposable? OnChange(Action<SlotGatingOptions, string?> listener) => null;
    }

    private sealed class Audit : IAuthAuditWriter
    {
        public List<AuthAuditLog> Entries { get; } = [];

        public Task AddAsync(AuthAuditLog log, CancellationToken ct = default)
        {
            Entries.Add(log);
            return Task.CompletedTask;
        }
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(1);
    }

    private sealed class RequestContext : IRequestContext
    {
        public string? IpAddress => "203.0.113.10";
        public string? UserAgent => "test-agent";
    }

    private sealed class CorrelationContext : ICorrelationContext
    {
        public string CorrelationId => "test-correlation";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
