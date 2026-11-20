using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using BuildingBlocks.TenantTimeZones;
using BuildingBlocks.Web.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Mobile.Application.Projections.Consumers;
using TaxVision.Mobile.Application.RateLimiting;
using TaxVision.Mobile.Application.RateLimiting.Abstractions;
using TaxVision.Mobile.Application.RateLimiting.Consumers;
using TaxVision.Mobile.Domain.Projections;
using TaxVision.Mobile.Domain.RateLimiting;
using TaxVision.Mobile.Infrastructure.Persistence;
using TaxVision.Mobile.Infrastructure.Persistence.Repositories;
using TaxVision.Mobile.Infrastructure.RateLimiting;

namespace TaxVision.Mobile.Tests.Infrastructure;

/// <summary>Plan del tenant (gate de mileage), proyección de roles y su reparación por pull.</summary>
public sealed class TenantPlanAndRoleProjectionTests : IDisposable
{
    private readonly MobileDbContext _db = new(
        new DbContextOptionsBuilder<MobileDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new NoTenant()
    );
    private readonly RecordingInvalidator _cache = new();
    private readonly FakeSnapshots _snapshots = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Plan_event_creates_the_row_with_its_modules_and_invalidates_the_cache()
    {
        var tenant = Guid.NewGuid();

        await ConsumePlanAsync(tenant, revision: 3, ("module.mileage", "True"), ("module.campaigns", "False"));

        Assert.Equal(["mileage"], await ReadModulesAsync(tenant));
        Assert.Equal([tenant], _cache.Invalidated);
    }

    [Fact]
    public async Task Older_plan_event_is_ignored_and_a_newer_empty_one_denies()
    {
        var tenant = Guid.NewGuid();
        await ConsumePlanAsync(tenant, revision: 5, ("module.mileage", "True"));

        await ConsumePlanAsync(tenant, revision: 4, ("module.mileage", "False"));
        Assert.Equal(["mileage"], await ReadModulesAsync(tenant));

        await ConsumePlanAsync(tenant, revision: 6, ("module.mileage", "False"));
        Assert.Empty((await ReadModulesAsync(tenant))!);
    }

    [Fact]
    public async Task Without_a_row_the_reader_returns_null_not_an_empty_list()
    {
        // null no gatea (todavía no llegó el plan); [] deniega (se sabe que no tiene módulos).
        Assert.Null(await ReadModulesAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Role_event_keeps_the_newest_version_and_never_touches_user_codes()
    {
        var tenant = Guid.NewGuid();
        var role = Guid.NewGuid();
        var user = UserPermissionsProjection.Create(
            tenant,
            Guid.NewGuid(),
            4,
            ["mobile.trip.read"],
            [role],
            DateTime.UtcNow
        );
        _db.UserPermissionsProjections.Add(user);
        await _db.SaveChangesAsync();

        await ConsumeRoleAsync(tenant, role, version: 2, "mobile.trip.read", "mobile.trip.manage");
        await ConsumeRoleAsync(tenant, role, version: 1, "mobile.trip.read");

        var row = await _db.RolePermissionsProjections.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((role, 2), (row.Id, row.PermissionsVersion));
        Assert.Equal(["mobile.trip.read", "mobile.trip.manage"], row.PermissionCodes());
        Assert.Equal(
            ["mobile.trip.read"],
            (await _db.UserPermissionsProjections.IgnoreQueryFilters().SingleAsync()).PermissionCodes()
        );
    }

    [Fact]
    public async Task Refresh_creates_a_missing_row_and_updates_a_stale_one()
    {
        var tenant = Guid.NewGuid();
        _snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 3, ["mileage"]);

        Assert.True(await Resolver().RefreshAsync(tenant));
        Assert.Equal(["mileage"], await ReadModulesAsync(tenant));

        _snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 4, []);
        Assert.True(await Resolver().RefreshAsync(tenant));
        var row = await _db.TenantPlanCodeProjections.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(4, row.RevisionNumber);
        Assert.Empty(row.EnabledModules);
        Assert.Equal([tenant, tenant], _cache.Invalidated);
    }

    [Fact]
    public async Task Refresh_never_overwrites_a_newer_row()
    {
        var tenant = Guid.NewGuid();
        var row = TenantPlanCodeProjection.Create(tenant, "enterprise", 9);
        row.ApplyIfNewer("enterprise", 9, ["mileage"]);
        _db.TenantPlanCodeProjections.Add(row);
        await _db.SaveChangesAsync();
        _snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 8, []);

        Assert.True(await Resolver().RefreshAsync(tenant));

        Assert.Equal(["mileage"], await ReadModulesAsync(tenant));
    }

    [Fact]
    public async Task Refresh_without_a_snapshot_reports_failure_and_writes_nothing()
    {
        // Subscription caído o sin token: la pasada queda incompleta y el job reintenta pronto.
        _snapshots.Snapshot = null;

        Assert.False(await Resolver().RefreshAsync(Guid.NewGuid()));
        Assert.Empty(_db.TenantPlanCodeProjections.IgnoreQueryFilters());
    }

    [Fact]
    public async Task Job_targets_known_tenants_without_a_plan_row_or_with_a_stale_one()
    {
        var missing = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var stale = Guid.NewGuid();
        foreach (var tenant in new[] { missing, fresh, stale })
            _db.UserPermissionsProjections.Add(
                UserPermissionsProjection.Create(tenant, Guid.NewGuid(), 1, ["mobile.trip.read"], [], DateTime.UtcNow)
            );
        _db.TenantPlanCodeProjections.Add(TenantPlanCodeProjection.Create(fresh, "enterprise", 1));
        _db.TenantPlanCodeProjections.Add(TenantPlanCodeProjection.Create(stale, "enterprise", 1));
        await _db.SaveChangesAsync();
        var targets = new TenantEntitlementsRefreshTargets(_db);

        var all = await targets.ListTenantsToRefreshAsync(DateTime.UtcNow.AddHours(1), 10, CancellationToken.None);
        var missingOnly = await targets.ListTenantsToRefreshAsync(
            DateTime.UtcNow.AddHours(-1),
            10,
            CancellationToken.None
        );

        Assert.Equal(new[] { missing, fresh, stale }.Order(), all.Order());
        Assert.Equal([missing], missingOnly);
    }

    // Servicio recién nacido: sin personal ni planes proyectados, las oficinas solo están en el directorio de Tenant.
    [Fact]
    public async Task Job_targets_offices_known_only_from_the_tenant_directory()
    {
        var office = Guid.NewGuid();
        _db.Set<TenantTimeZoneProjection>()
            .Add(TenantTimeZoneProjection.Create(office, "America/New_York", null, DateTime.UtcNow));
        await _db.SaveChangesAsync();
        var targets = new TenantEntitlementsRefreshTargets(_db);

        var result = await targets.ListTenantsToRefreshAsync(DateTime.UtcNow, 10, CancellationToken.None);

        Assert.Equal([office], result);
    }

    [Fact]
    public async Task Without_any_known_office_the_targets_report_it()
    {
        var targets = new TenantEntitlementsRefreshTargets(_db);

        Assert.False(await targets.AnyKnownTenantAsync(CancellationToken.None));
    }

    private TenantEntitlementsResolver Resolver() =>
        new(new TenantPlanCodeProjectionRepository(_db), _snapshots, _cache, _db);

    private Task<IReadOnlyList<string>?> ReadModulesAsync(Guid tenant) =>
        new EfTenantEntitlementModulesReader(_db).GetEnabledModulesAsync(tenant);

    private async Task ConsumePlanAsync(Guid tenant, long revision, params (string Key, string Value)[] entries)
    {
        await TenantPlanCodeProjectionConsumer.Handle(
            new TenantEntitlementsChangedIntegrationEvent
            {
                TenantId = tenant,
                CorrelationId = "plan",
                RevisionNumber = revision,
                ChangedKeys = [],
                PlanCode = "enterprise",
                SubscriptionStatus = "Active",
                SeatCount = 3,
                AvailableSeatCount = 2,
                EntitlementValues = entries.ToDictionary(e => e.Key, e => e.Value),
            },
            new TenantPlanCodeProjectionRepository(_db),
            _cache,
            _db,
            new CorrelationContext(),
            NullLogger<TenantPlanCodeProjection>.Instance,
            CancellationToken.None
        );
    }

    private Task ConsumeRoleAsync(Guid tenant, Guid role, int version, params string[] codes) =>
        RolePermissionsChangedProjectionConsumer.Handle(
            new RolePermissionsChangedIntegrationEvent
            {
                TenantId = tenant,
                CorrelationId = "role",
                RoleId = role,
                RoleName = "Employee",
                PermissionsVersion = version,
                PermissionCodes = codes,
            },
            new MobileProjectionRepository(_db),
            _db,
            new CorrelationContext(),
            CancellationToken.None
        );

    private sealed class FakeSnapshots : ITenantEntitlementsSnapshotClient
    {
        public TenantEntitlementsSnapshot? Snapshot { get; set; }

        public Task<TenantEntitlementsSnapshot?> FetchAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(Snapshot);
    }

    private sealed class RecordingInvalidator : ITenantPlanCodeCacheInvalidator
    {
        public List<Guid> Invalidated { get; } = [];

        public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default)
        {
            Invalidated.Add(tenantId);
            return Task.CompletedTask;
        }
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
