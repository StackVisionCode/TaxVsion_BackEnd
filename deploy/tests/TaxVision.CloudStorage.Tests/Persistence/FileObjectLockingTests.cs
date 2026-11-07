using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
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

    // CST-6: dos escrituras sobre la misma fila chocan (escaneo contra borrado); el token es solo RowVersion.
    [Theory]
    [InlineData(typeof(FileObject))]
    [InlineData(typeof(Folder))]
    public void RowVersion_is_the_only_concurrency_token(Type entity)
    {
        using var db = CreateContext();

        var tokens = db
            .Model.FindEntityType(entity)!
            .GetProperties()
            .Where(property => property.IsConcurrencyToken)
            .Select(property => property.Name);

        Assert.Equal(["RowVersion"], tokens);
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

    [Fact]
    public async Task Locked_folder_read_refuses_to_run_without_an_open_transaction()
    {
        await using var db = CreateContext();
        var repository = new FolderRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.GetForUpdateAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None)
        );
    }

    [Fact]
    public async Task Claiming_a_scan_refuses_to_run_without_an_open_transaction()
    {
        await using var db = CreateContext();
        var repository = new FileObjectRepository(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            repository.TryClaimScanAsync(Guid.NewGuid(), CancellationToken.None)
        );
    }
}
