using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Persistence;

public sealed class FileObjectLockingTests
{
    private sealed class FakeTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private static CloudStorageDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new FakeTenantContext()
        );

    // Un token global hace que cualquier escritura (scan, legal hold, mover) choque con otra.
    [Fact]
    public void FileObject_has_no_global_concurrency_tokens()
    {
        using var db = CreateContext();

        var tokens = db
            .Model.FindEntityType(typeof(FileObject))!
            .GetProperties()
            .Where(property => property.IsConcurrencyToken)
            .Select(property => property.Name);

        Assert.Empty(tokens);
    }

    [Fact]
    public async Task Locked_read_refuses_to_run_without_an_open_transaction()
    {
        await using var db = CreateContext();
        var repository = new FileObjectRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.GetForUpdateAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)
        );
    }
}
