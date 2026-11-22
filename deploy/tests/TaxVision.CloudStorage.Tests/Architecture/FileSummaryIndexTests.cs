using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;

namespace TaxVision.CloudStorage.Tests.Architecture;

/// <summary>Índices del resumen Documents medidos con volumen: los últimos y los pendientes de revisión sin lookups.</summary>
public sealed class FileSummaryIndexTests
{
    [Fact]
    public void The_recent_index_covers_the_latest_rows()
    {
        var index = FindIndex("IX_Files_Tenant_Created");

        Assert.Contains(nameof(FileObject.OriginalName), index.GetIncludeProperties()!);
    }

    [Fact]
    public void The_status_index_covers_the_documents_branch()
    {
        var index = FindIndex("IX_Files_TenantId_Status_CreatedAtUtc");

        Assert.Equal(
            [nameof(FileObject.TenantId), nameof(FileObject.Status), nameof(FileObject.CreatedAtUtc)],
            index.Properties.Select(p => p.Name)
        );
        Assert.Equal(
            [
                nameof(FileObject.OwnerType),
                nameof(FileObject.OwnerId),
                nameof(FileObject.FolderType),
                nameof(FileObject.SourceResourceType),
            ],
            index.GetIncludeProperties()!
        );
    }

    private static IIndex FindIndex(string name)
    {
        using var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>()
                .UseSqlServer("Server=model-only;Database=model-only")
                .Options,
            new NoTenant()
        );
        var index = db.GetService<IDesignTimeModel>()
            .Model.FindEntityType(typeof(FileObject))!
            .GetIndexes()
            .SingleOrDefault(i => i.GetDatabaseName() == name);
        Assert.NotNull(index);
        return index;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }
}
