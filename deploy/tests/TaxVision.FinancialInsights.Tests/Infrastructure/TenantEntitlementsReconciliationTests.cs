using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Processing;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Infrastructure;

public sealed class TenantEntitlementsReconciliationTests
{
    [Fact]
    public async Task Refresh_creates_a_missing_row_and_updates_a_stale_one()
    {
        var tenant = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        h.Snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 3, ["financial_insights"], 150);

        Assert.True(await h.Entitlements.RefreshAsync(tenant));
        Assert.Equal(150, (await h.Db.TenantEntitlementsProjections.SingleAsync()).DocumentsPerMonth);

        h.Snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 4, [], 50);
        Assert.True(await h.Entitlements.RefreshAsync(tenant));
        var row = await h.Db.TenantEntitlementsProjections.SingleAsync();
        Assert.Equal(4, row.RevisionNumber);
        Assert.Empty(row.EnabledModules);
        Assert.Equal(2, h.Cache.Invalidated.Count);

        h.Snapshots.Snapshot = null;
        Assert.False(await h.Entitlements.RefreshAsync(tenant));
    }

    [Fact]
    public async Task Job_targets_known_tenants_without_a_row_or_with_a_stale_one()
    {
        var known = Guid.NewGuid();
        var withBatch = Guid.NewGuid();
        var fresh = Guid.NewGuid();
        var stale = Guid.NewGuid();
        await using var h = new FinancialTestHarness(known);
        h.Db.TenantDirectoryProjections.Add(TenantDirectoryProjection.Create(known, "Known", DateTime.UtcNow));
        h.Db.TenantDirectoryProjections.Add(TenantDirectoryProjection.Create(fresh, "Fresh", DateTime.UtcNow));
        h.Db.TenantDirectoryProjections.Add(TenantDirectoryProjection.Create(stale, "Stale", DateTime.UtcNow));
        h.Db.TenantEntitlementsProjections.Add(TenantEntitlementsProjection.Create(fresh, "p", 1, [], null));
        h.Db.TenantEntitlementsProjections.Add(TenantEntitlementsProjection.Create(stale, "p", 1, [], null));
        h.Db.ImportBatches.Add(ImportBatch.Create(withBatch, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]).Value);
        await h.Db.SaveChangesAsync();
        var queue = new ImportProcessingQueue(h.Db);

        var due = await queue.ListTenantsToRefreshAsync(staleBeforeUtc: DateTime.UtcNow.AddHours(1), take: 10, CancellationToken.None);
        var missingOnly = await queue.ListTenantsToRefreshAsync(staleBeforeUtc: DateTime.UtcNow.AddHours(-1), take: 10, CancellationToken.None);

        Assert.Equal(new[] { known, withBatch, fresh, stale }.Order(), due.Order());
        Assert.Equal(new[] { known, withBatch }.Order(), missingOnly.Order());
    }

    [Fact]
    public async Task Worker_finds_tenants_whose_ready_documents_wait_for_a_plan()
    {
        var waiting = Guid.NewGuid();
        await using var h = new FinancialTestHarness(waiting);
        var batch = ImportBatch.Create(waiting, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]).Value;
        batch.MarkDocumentReady(batch.Documents[0].Id);
        await h.Batches.TryAddAsync(batch, CancellationToken.None);
        var queue = new ImportProcessingQueue(h.Db);

        Assert.Equal([waiting], await queue.ListTenantsWaitingForPlanAsync(5, CancellationToken.None));
        await h.SeedTenantAsync();
        Assert.Empty(await queue.ListTenantsWaitingForPlanAsync(5, CancellationToken.None));
    }

    [Fact]
    public async Task Portal_import_heals_a_missing_plan_row_instead_of_failing()
    {
        var tenant = Guid.NewGuid();
        var customer = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        await h.SeedCustomerAsync(customer);
        h.Snapshots.Snapshot = new TenantEntitlementsSnapshot("enterprise", 2, ["financial_insights", "documents"], 200);

        var created = await CreatePortalImportBatchHandler.Handle(
            new CreatePortalImportBatchCommand(tenant, Guid.NewGuid(), customer, [Guid.NewGuid()], true),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Db,
            CancellationToken.None
        );

        Assert.True(created.IsSuccess);
        Assert.Equal(1, h.Snapshots.Calls);
    }
}
