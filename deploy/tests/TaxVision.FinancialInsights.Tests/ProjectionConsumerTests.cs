using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Api.Events;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class ProjectionConsumerTests
{
    [Fact]
    public async Task User_deny_event_removes_permission_and_role_event_cannot_restore_it()
    {
        var tenant = Guid.NewGuid(); var user = Guid.NewGuid(); var role = Guid.NewGuid();
        await using var db = Database(tenant);
        var granted = new UserRolesChangedIntegrationEvent { TenantId = tenant, UserId = user,
            PermissionsVersion = 1, ActorType = "TenantEmployee", PermissionCodes = ["financial.import"], RoleIds = [role] };
        await ProjectionConsumer.Handle(granted, db, CancellationToken.None);
        var reader = new FinancialUserPermissionsReader(db);
        Assert.Contains("financial.import", (await reader.GetSnapshotAsync(tenant, user))!.PermissionCodes);

        await ProjectionConsumer.Handle(granted with { PermissionsVersion = 2, PermissionCodes = [] }, db, CancellationToken.None);
        await ProjectionConsumer.Handle(granted, db, CancellationToken.None);
        await ProjectionConsumer.Handle(new RolePermissionsChangedIntegrationEvent { TenantId = tenant,
            RoleId = role, RoleName = "Preparer", PermissionsVersion = 3,
            PermissionCodes = ["financial.import"] }, db, CancellationToken.None);
        Assert.Empty((await reader.GetSnapshotAsync(tenant, user))!.PermissionCodes);

        await ProjectionConsumer.Handle(new UserDeactivatedIntegrationEvent { TenantId = tenant,
            UserId = user, ActorType = "TenantEmployee", Email = "test@example.com" }, db, CancellationToken.None);
        await ProjectionConsumer.Handle(granted with { PermissionsVersion = 3 }, db, CancellationToken.None);
        Assert.Null(await reader.GetSnapshotAsync(tenant, user));
    }

    [Fact]
    public async Task Tenant_module_projection_keeps_newest_revision_and_empty_means_disabled()
    {
        var tenant = Guid.NewGuid();
        await using var db = Database(tenant);
        var cache = new RecordingCache();
        var invalidator = new FinancialEntitlementsCacheInvalidator(
            new CachedTenantPlanCodeReader(cache, new FinancialTenantPlanCodeReader(db)),
            new CachedTenantEntitlementModulesReader(cache, new FinancialTenantModulesReader(db)));
        var enabled = Entitlements(tenant, 1, "True");
        await ProjectionConsumer.Handle(enabled, db, invalidator, CancellationToken.None);
        await ProjectionConsumer.Handle(Entitlements(tenant, 2, "False"), db, invalidator, CancellationToken.None);
        await ProjectionConsumer.Handle(enabled, db, invalidator, CancellationToken.None);
        var modules = await new FinancialTenantModulesReader(db).GetEnabledModulesAsync(tenant);
        Assert.Empty(modules!);
        Assert.Equal("test", await new FinancialTenantPlanCodeReader(db).GetPlanCodeAsync(tenant));
        Assert.Contains($"ratelimit:plancode:{tenant:N}", cache.RemovedKeys);
        Assert.Equal(6, cache.RemovedKeys.Count);
    }

    [Fact]
    public async Task Customer_status_projection_is_tenant_scoped_and_update_does_not_reactivate()
    {
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid();
        await using var db = Database(tenant);
        var time = DateTime.UtcNow;
        await ProjectionConsumer.Handle(new CustomerCreatedIntegrationEvent { TenantId = tenant,
            CustomerId = customer, DisplayName = "Client", Kind = "Individual", PrimaryEmail = "a@example.com",
            Language = "En", CreatedByUserId = Guid.NewGuid(), OccurredOn = time }, db, CancellationToken.None);
        await ProjectionConsumer.Handle(new CustomerDeactivatedIntegrationEvent { TenantId = tenant,
            CustomerId = customer, DeactivatedByUserId = Guid.NewGuid(), DeactivatedAtUtc = time.AddSeconds(1) }, db, CancellationToken.None);
        await ProjectionConsumer.Handle(new CustomerUpdatedIntegrationEvent { TenantId = tenant,
            CustomerId = customer, DisplayName = "Renamed", PrimaryEmail = "a@example.com", Language = "En",
            ModifiedByUserId = Guid.NewGuid(), OccurredOn = time.AddSeconds(2) }, db, CancellationToken.None);
        var row = await db.CustomerDirectoryProjections.SingleAsync();
        Assert.False(row.IsActive);
        Assert.Equal("Renamed", row.DisplayName);
    }

    [Fact]
    public async Task Tenant_status_does_not_roll_back_on_older_event()
    {
        var tenant = Guid.NewGuid();
        await using var db = Database(tenant);
        await ProjectionConsumer.Handle(new TenantStatusChangedIntegrationEvent { TenantId = tenant,
            ChangedTenantId = tenant, Status = "Suspended", IsActive = false, TenantVersion = 2 },
            db, CancellationToken.None);
        await ProjectionConsumer.Handle(new TenantStatusChangedIntegrationEvent { TenantId = tenant,
            ChangedTenantId = tenant, Status = "Active", IsActive = true, TenantVersion = 1 },
            db, CancellationToken.None);
        Assert.False((await db.TenantDirectoryProjections.SingleAsync()).IsActive);
    }

    private static TenantEntitlementsChangedIntegrationEvent Entitlements(Guid tenant, long revision, string value) =>
        new() { TenantId = tenant, RevisionNumber = revision, ChangedKeys = ["module.financial_insights"],
            PlanCode = "test", SubscriptionStatus = "Active", SeatCount = 1, AvailableSeatCount = 1,
            EntitlementValues = new Dictionary<string, string> { ["module.financial_insights"] = value } };

    private static FinancialInsightsDbContext Database(Guid tenant) => new(
        new DbContextOptionsBuilder<FinancialInsightsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new TenantContext(tenant));

    private sealed class TenantContext(Guid tenant) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenant;
        public bool HasTenant => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }

    private sealed class RecordingCache : ICacheService
    {
        public List<string> RemovedKeys { get; } = [];
        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));
        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) => Task.CompletedTask;
        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }
        public Task<T> GetOrCreateAsync<T>(string key, Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null, CancellationToken ct = default) => factory(ct);
    }
}
