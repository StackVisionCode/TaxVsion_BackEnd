using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.FinancialInsights.Api.Events;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.FinancialInsights.Tests;

public sealed class ImportBatchTests
{
    [Fact]
    public void Rejects_duplicate_file_ids_and_oversized_batches()
    {
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid(); var user = Guid.NewGuid(); var file = Guid.NewGuid();
        Assert.Throws<ArgumentException>(() => ImportBatch.Create(tenant, customer, user, [file, file]));
        Assert.Throws<ArgumentException>(() => ImportBatch.Create(tenant, customer, user, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid())));
    }

    [Fact]
    public async Task Scan_event_only_activates_file_for_matching_customer_and_is_idempotent()
    {
        var tenant = Guid.NewGuid(); var customer = Guid.NewGuid(); var file = Guid.NewGuid();
        var batch = ImportBatch.Create(tenant, customer, Guid.NewGuid(), [file]);
        var tenantContext = new TestTenant(tenant);
        var options = new DbContextOptionsBuilder<FinancialInsightsDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        await using var db = new FinancialInsightsDbContext(options, tenantContext);
        db.ImportBatches.Add(batch); await db.SaveChangesAsync();

        var wrongCustomer = new FileAvailableIntegrationEvent { TenantId = tenant, FileId = file, OwnerType = "Customer", OwnerId = Guid.NewGuid(), ObjectKey = "x", ContentType = "application/pdf", SizeBytes = 1, ChecksumSha256 = "x", CreatedBy = Guid.NewGuid() };
        await FileScanConsumer.Handle(wrongCustomer, db, CancellationToken.None);
        Assert.Equal(ImportDocumentStatus.AwaitingScan, batch.Documents.Single().Status);

        var valid = wrongCustomer with { OwnerId = customer };
        tenantContext.SetTenant(Guid.NewGuid());
        await FileScanConsumer.Handle(valid, db, CancellationToken.None);
        Assert.Equal(ImportDocumentStatus.AwaitingScan, batch.Documents.Single().Status);
        tenantContext.SetTenant(tenant);
        await FileScanConsumer.Handle(valid, db, CancellationToken.None);
        await FileScanConsumer.Handle(valid, db, CancellationToken.None);
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents.Single().Status);
    }

    private sealed class TestTenant(Guid tenant) : ITenantContext
    {
        public Guid TenantId { get; private set; } = tenant;
        public bool HasTenant => TenantId != Guid.Empty;
        public void SetTenant(Guid tenantId) => TenantId = tenantId;
    }
}
