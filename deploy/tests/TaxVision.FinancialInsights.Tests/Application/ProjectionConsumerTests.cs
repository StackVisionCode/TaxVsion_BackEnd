using BuildingBlocks.Caching;
using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports.Consumers;
using TaxVision.FinancialInsights.Application.Projections.Consumers;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class ProjectionConsumerTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    [Fact]
    public async Task User_deny_event_removes_permission_and_role_event_cannot_restore_it()
    {
        var tenant = Guid.NewGuid();
        var user = Guid.NewGuid();
        var role = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var granted = new UserRolesChangedIntegrationEvent
        {
            TenantId = tenant,
            UserId = user,
            PermissionsVersion = 1,
            ActorType = "TenantEmployee",
            PermissionCodes = ["financial.import"],
            RoleIds = [role],
        };
        await ProjectionConsumer.Handle(granted, h.Projections, h.Db, h.Correlation, Ct);
        var reader = new FinancialUserPermissionsReader(h.Db);
        Assert.Contains("financial.import", (await reader.GetSnapshotAsync(tenant, user))!.PermissionCodes);

        await ProjectionConsumer.Handle(granted with { PermissionsVersion = 2, PermissionCodes = [] }, h.Projections, h.Db, h.Correlation, Ct);
        await ProjectionConsumer.Handle(granted, h.Projections, h.Db, h.Correlation, Ct);
        await ProjectionConsumer.Handle(
            new RolePermissionsChangedIntegrationEvent
            {
                TenantId = tenant,
                RoleId = role,
                RoleName = "Preparer",
                PermissionsVersion = 3,
                PermissionCodes = ["financial.import"],
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );
        Assert.Empty((await reader.GetSnapshotAsync(tenant, user))!.PermissionCodes);

        await UserLifecyclePermissionsProjectionConsumer.Handle(
            new UserDeactivatedIntegrationEvent
            {
                TenantId = tenant,
                UserId = user,
                ActorType = "TenantEmployee",
                Email = "test@example.com",
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );
        await ProjectionConsumer.Handle(granted with { PermissionsVersion = 3 }, h.Projections, h.Db, h.Correlation, Ct);
        Assert.Null(await reader.GetSnapshotAsync(tenant, user));
    }

    [Fact]
    public async Task Tenant_module_projection_keeps_newest_revision_and_empty_means_disabled()
    {
        var tenant = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var cache = new RecordingCache();
        var invalidator = new TaxVision.FinancialInsights.Infrastructure.RateLimiting.TenantPlanCodeCacheInvalidator(
            new CachedTenantPlanCodeReader(cache, new FinancialTenantPlanCodeReader(h.Db)),
            new CachedTenantEntitlementModulesReader(cache, new FinancialTenantModulesReader(h.Db))
        );
        var enabled = Entitlements(tenant, 1, "True");
        await ProjectionConsumer.Handle(enabled, h.Projections, h.Db, invalidator, h.Correlation, Ct);
        await ProjectionConsumer.Handle(Entitlements(tenant, 2, "False"), h.Projections, h.Db, invalidator, h.Correlation, Ct);
        await ProjectionConsumer.Handle(enabled, h.Projections, h.Db, invalidator, h.Correlation, Ct);

        Assert.Empty((await new FinancialTenantModulesReader(h.Db).GetEnabledModulesAsync(tenant))!);
        Assert.Equal("test", await new FinancialTenantPlanCodeReader(h.Db).GetPlanCodeAsync(tenant));
        Assert.Contains($"ratelimit:plancode:{tenant:N}", cache.RemovedKeys);
        Assert.Equal(6, cache.RemovedKeys.Count);
    }

    [Fact]
    public async Task Entitlements_store_the_monthly_document_quota_when_the_plan_sends_it()
    {
        var tenant = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var withQuota = Entitlements(tenant, 1, "True") with
        {
            EntitlementValues = new Dictionary<string, string>
            {
                ["module.financial_insights"] = "True",
                ["financial.documents_per_month"] = "200",
            },
        };
        await ProjectionConsumer.Handle(withQuota, h.Projections, h.Db, h.Cache, h.Correlation, Ct);
        Assert.Equal(200, (await h.Db.TenantEntitlementsProjections.SingleAsync()).DocumentsPerMonth);

        await ProjectionConsumer.Handle(Entitlements(tenant, 2, "True"), h.Projections, h.Db, h.Cache, h.Correlation, Ct);
        Assert.Null((await h.Db.TenantEntitlementsProjections.SingleAsync()).DocumentsPerMonth);
    }

    [Fact]
    public async Task Customer_status_projection_is_tenant_scoped_and_update_does_not_reactivate()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var time = DateTime.UtcNow;
        await ProjectionConsumer.Handle(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = tenant,
                CustomerId = customer,
                DisplayName = "Client",
                Kind = "Individual",
                PrimaryEmail = "a@example.com",
                Language = "En",
                CreatedByUserId = Guid.NewGuid(),
                OccurredOn = time,
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );
        await ProjectionConsumer.Handle(
            new CustomerDeactivatedIntegrationEvent
            {
                TenantId = tenant,
                CustomerId = customer,
                DeactivatedByUserId = Guid.NewGuid(),
                DeactivatedAtUtc = time.AddSeconds(1),
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );
        await ProjectionConsumer.Handle(
            new CustomerUpdatedIntegrationEvent
            {
                TenantId = tenant,
                CustomerId = customer,
                DisplayName = "Renamed",
                PrimaryEmail = "a@example.com",
                Language = "En",
                ModifiedByUserId = Guid.NewGuid(),
                OccurredOn = time.AddSeconds(2),
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );

        var row = await h.Db.CustomerDirectoryProjections.SingleAsync();
        Assert.False(row.IsActive);
        Assert.Equal("Renamed", row.DisplayName);
    }

    [Fact]
    public async Task Bulk_import_adds_missing_customers_without_touching_known_ones()
    {
        var tenant = Guid.NewGuid();
        var known = Guid.NewGuid();
        var created = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        await h.SeedCustomerAsync(known, active: false, name: "Known");

        await ProjectionConsumer.Handle(
            new CustomersBulkImportedIntegrationEvent
            {
                TenantId = tenant,
                ImportJobId = Guid.NewGuid(),
                CreatedByUserId = Guid.NewGuid(),
                CompletedAtUtc = DateTime.UtcNow,
                TotalRows = 2,
                SuccessCount = 1,
                UpdatedCount = 1,
                SkippedCount = 0,
                FailedCount = 0,
                CreatedCustomerIds = [created],
                UpdatedCustomerIds = [known],
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );

        var rows = await h.Db.CustomerDirectoryProjections.ToDictionaryAsync(r => r.CustomerId);
        Assert.True(rows[created].IsActive);
        Assert.Null(rows[created].DisplayName);
        Assert.False(rows[known].IsActive);
        Assert.Equal("Known", rows[known].DisplayName);
    }

    [Fact]
    public async Task Tenant_status_does_not_roll_back_on_older_event()
    {
        var tenant = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        await ProjectionConsumer.Handle(
            new TenantStatusChangedIntegrationEvent
            {
                TenantId = tenant,
                ChangedTenantId = tenant,
                Status = "Suspended",
                IsActive = false,
                TenantVersion = 2,
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );
        await ProjectionConsumer.Handle(
            new TenantStatusChangedIntegrationEvent
            {
                TenantId = tenant,
                ChangedTenantId = tenant,
                Status = "Active",
                IsActive = true,
                TenantVersion = 1,
            },
            h.Projections,
            h.Db,
            h.Correlation,
            Ct
        );

        Assert.False((await h.Db.TenantDirectoryProjections.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Scan_event_only_activates_file_for_matching_tenant_and_customer_and_is_idempotent()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        var file = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var batch = ImportBatch.Create(tenant, customer, Guid.NewGuid(), [file]).Value;
        Assert.True(await h.Batches.TryAddAsync(batch, Ct));

        var wrongCustomer = new FileAvailableIntegrationEvent
        {
            TenantId = tenant,
            FileId = file,
            OwnerType = "Customer",
            OwnerId = Guid.NewGuid(),
            ObjectKey = "x",
            ContentType = "application/pdf",
            SizeBytes = 1,
            ChecksumSha256 = "x",
            CreatedBy = Guid.NewGuid(),
        };
        await FileScanConsumer.Handle(wrongCustomer, h.Batches, h.Db, h.Correlation, Ct);
        await FileScanConsumer.Handle(wrongCustomer with { OwnerId = customer, TenantId = Guid.NewGuid() }, h.Batches, h.Db, h.Correlation, Ct);
        Assert.Equal(ImportDocumentStatus.AwaitingScan, batch.Documents.Single().Status);

        var valid = wrongCustomer with { OwnerId = customer };
        await FileScanConsumer.Handle(valid, h.Batches, h.Db, h.Correlation, Ct);
        await FileScanConsumer.Handle(valid, h.Batches, h.Db, h.Correlation, Ct);
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents.Single().Status);

        await FileScanConsumer.Handle(
            new FileInfectedDetectedIntegrationEvent { TenantId = tenant, FileId = file, ObjectKey = "x", ScanReport = "eicar" },
            h.Batches,
            h.Db,
            h.Correlation,
            Ct
        );
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents.Single().Status);
    }

    private static TenantEntitlementsChangedIntegrationEvent Entitlements(Guid tenant, long revision, string value) =>
        new()
        {
            TenantId = tenant,
            RevisionNumber = revision,
            ChangedKeys = ["module.financial_insights"],
            PlanCode = "test",
            SubscriptionStatus = "Active",
            SeatCount = 1,
            AvailableSeatCount = 1,
            EntitlementValues = new Dictionary<string, string> { ["module.financial_insights"] = value },
        };

    private sealed class RecordingCache : ICacheService
    {
        public List<string> RemovedKeys { get; } = [];

        public Task<T?> GetAsync<T>(string key, CancellationToken ct = default) => Task.FromResult(default(T));

        public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(string key, CancellationToken ct = default)
        {
            RemovedKeys.Add(key);
            return Task.CompletedTask;
        }

        public Task<T> GetOrCreateAsync<T>(
            string key,
            Func<CancellationToken, Task<T>> factory,
            TimeSpan? ttl = null,
            CancellationToken ct = default
        ) => factory(ct);
    }
}
