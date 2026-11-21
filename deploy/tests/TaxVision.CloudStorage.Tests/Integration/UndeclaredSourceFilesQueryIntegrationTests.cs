using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>Contra el SQL Server real: archivos disponibles sin origen declarado, por keyset (TenantId, Id).</summary>
public sealed class UndeclaredSourceFilesQueryIntegrationTests : IAsyncLifetime
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly List<UndeclaredSourceFile> _pending = [];
    private readonly List<Guid> _excluded = [];

    private readonly string _connectionString = CloudStorageIntegrationDatabase.ConnectionString;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    public async Task InitializeAsync()
    {
        await using var db = NewContext();
        foreach (var tenant in new[] { _tenantA, _tenantB })
        {
            for (var i = 0; i < 3; i++)
            {
                var file = NewFile(tenant, available: true);
                db.Files.Add(file);
                _pending.Add(new UndeclaredSourceFile(tenant, file.Id));
            }

            var declared = NewFile(tenant, available: true);
            declared.DeclareSource(
                FileSourceDeclaration.Create("billing", "invoice", Guid.NewGuid(), "invoice_pdf", null).Value,
                Now
            );
            var deleted = NewFile(tenant, available: true);
            deleted.SoftDelete(Now, TimeSpan.FromDays(30));
            var unavailable = NewFile(tenant, available: false);
            db.Files.AddRange(declared, deleted, unavailable);
            _excluded.AddRange([declared.Id, deleted.Id, unavailable.Id]);
        }
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db
            .Files.IgnoreQueryFilters()
            .Where(f => f.TenantId == _tenantA || f.TenantId == _tenantB)
            .ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Pages_through_pending_files_of_every_tenant_without_repeating()
    {
        await using var db = NewContext();
        var repository = new FileObjectRepository(db);
        var seen = new List<UndeclaredSourceFile>();
        UndeclaredSourceFile? after = null;
        while (true)
        {
            var page = await repository.ListUndeclaredSourceFilesAsync(after, 2, CancellationToken.None);
            if (page.Count == 0)
                break;
            seen.AddRange(page);
            after = page[^1];
        }

        var mine = seen.Where(f => f.TenantId == _tenantA || f.TenantId == _tenantB).ToList();
        Assert.Equal(_pending.OrderBy(f => f.Id), mine.OrderBy(f => f.Id));
        Assert.Equal(seen.Count, seen.Distinct().Count());
        Assert.DoesNotContain(mine, f => _excluded.Contains(f.Id));
        // Cada tenant sale junto, sin intercalarse.
        Assert.Equal(2, mine.Select(f => f.TenantId).Chunk(3).Count(c => c.Distinct().Count() == 1));
    }

    private static FileObject NewFile(Guid tenantId, bool available)
    {
        var id = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{tenantId:N}/tenant/documents/2026/{id:N}.pdf").Value;
        var file = FileObject
            .Register(
                id,
                tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "claim.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                Now,
                Now.AddHours(1)
            )
            .Value;
        if (!available)
            return file;
        file.MarkPendingScan(Now);
        file.MarkScanning();
        file.MarkAvailable(ChecksumSha256.Create(new string('a', 64)).Value, "application/pdf", Now);
        return file;
    }
}
