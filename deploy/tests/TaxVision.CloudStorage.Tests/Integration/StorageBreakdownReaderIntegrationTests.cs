using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>Columna calculada <c>Category</c> + GROUP BY contra SQL Server real (InMemory no evalúa columnas calculadas).</summary>
public sealed class StorageBreakdownReaderIntegrationTests : IAsyncLifetime
{
    private static readonly ChecksumSha256 Checksum = ChecksumSha256.Create(new string('a', 64)).Value;
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly DateTime _now = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    public async Task InitializeAsync()
    {
        var trashed = File("f.png", 70);
        Assert.True(trashed.SoftDelete(_now, TimeSpan.FromDays(30)).IsSuccess);
        await using var db = NewContext();
        db.Files.AddRange(
            File("a.pdf", 10),
            File("B.JPG", 20),
            File("c.mp4", 30),
            File("d.zip", 40),
            File("sinextension", 50),
            File("e.", 60),
            trashed,
            File("pending.pdf", 80, available: false),
            File("other.pdf", 90, tenant: _otherTenant)
        );
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.TenantId == _tenant || f.TenantId == _otherTenant)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Groups_used_files_by_category_like_the_crm()
    {
        await using var db = NewContext();

        var breakdown = await new StorageBreakdownReader(db).ReadAsync(_tenant, CancellationToken.None);

        Assert.Equal(
            [
                new CategoryUsage("Documents", 1, 10),
                new CategoryUsage("Images", 1, 20),
                new CategoryUsage("Video & Audio", 1, 30),
                new CategoryUsage("Others", 3, 150),
            ],
            breakdown.ByCategory
        );
        Assert.Equal(new TrashUsage(1, 70), breakdown.Trash);
    }

    [Fact]
    public async Task Category_column_is_deterministic()
    {
        await using var db = NewContext();

        var flags = await db
            .Database.SqlQueryRaw<int>(
                "SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'Files'), 'Category', 'IsDeterministic') AS int) AS [Value]"
                    + " UNION ALL SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'Files'), 'Category', 'IsIndexable') AS int)"
            )
            .ToListAsync();

        Assert.Equal([1, 1], flags);
    }

    private FileObject File(string name, long size, bool available = true, Guid? tenant = null)
    {
        var tenantId = tenant ?? _tenant;
        var id = Guid.NewGuid();
        var file = FileObject
            .Register(
                id,
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2025,
                ObjectKey.Create($"tenants/{tenantId:N}/Tenant/files/2025/{id:N}.bin").Value,
                name,
                "application/octet-stream",
                size,
                Guid.NewGuid(),
                _now,
                _now.AddHours(1)
            )
            .Value;
        if (!available)
            return file;
        file.MarkPendingScan(_now);
        file.MarkScanning();
        file.MarkAvailable(Checksum, "application/octet-stream", _now);
        return file;
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer(CloudStorageIntegrationDatabase.ConnectionString)
                .Options,
            new FixedTenant(_tenant)
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }
}
