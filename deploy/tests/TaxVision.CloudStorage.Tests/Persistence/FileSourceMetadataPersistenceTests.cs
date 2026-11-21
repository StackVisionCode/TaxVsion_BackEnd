using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Tests.Domain;

namespace TaxVision.CloudStorage.Tests.Persistence;

public sealed class FileSourceMetadataPersistenceTests
{
    private sealed class TenantContext(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid tenantId) { }
    }

    [Fact]
    public async Task The_declared_source_round_trips()
    {
        var file = FileObjectSourceTests.File(OwnerType.Invoice, Guid.NewGuid());
        var invoiceId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var declaredAt = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);
        file.DeclareSource(
            FileSourceDeclaration.Create("billing", "invoice", invoiceId, "invoice_pdf", customerId).Value,
            declaredAt
        );
        await using var db = NewContext(file.TenantId);
        db.Files.Add(file);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var stored = await db.Files.SingleAsync(f => f.Id == file.Id);

        Assert.Equal("billing", stored.SourceService);
        Assert.Equal("invoice", stored.SourceResourceType);
        Assert.Equal(invoiceId, stored.SourceResourceId);
        Assert.Equal("invoice_pdf", stored.SourceRole);
        Assert.Equal(customerId, stored.RelatedCustomerId);
        Assert.Equal(declaredAt, stored.SourceDeclaredAtUtc);
    }

    [Theory]
    [InlineData("IX_Files_Library_Recent", "[Status] = N'Available'")]
    [InlineData("IX_Files_Library_Customer", "[Status] = N'Available' AND [RelatedCustomerId] IS NOT NULL")]
    [InlineData("IX_Files_Library_Name", "[Status] = N'Available'")]
    [InlineData("IX_Files_Source_Resource", "[SourceResourceId] IS NOT NULL")]
    public void The_library_indexes_exist_with_their_filter(string name, string filter)
    {
        using var db = NewContext(Guid.NewGuid());

        var index = db.Model.FindEntityType(typeof(FileObject))!.GetIndexes().Single(i => i.GetDatabaseName() == name);

        Assert.Equal(filter, index.GetFilter());
    }

    private static CloudStorageDbContext NewContext(Guid tenantId) =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new TenantContext(tenantId)
        );
}
