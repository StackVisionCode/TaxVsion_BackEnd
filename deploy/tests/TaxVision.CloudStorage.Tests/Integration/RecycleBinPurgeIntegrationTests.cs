using System.Reflection;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Infrastructure.Storage;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>La purga diaria guarda un archivo por unidad: el conflicto de uno no deshace a los demás.</summary>
[Collection(CloudStorageConcurrencyCollection.Name)]
public sealed class RecycleBinPurgeIntegrationTests(CloudStorageConcurrencyFixture fixture)
{
    private const long FileSize = 4;
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task ConflictOnOneFile_DoesNotRollBackTheOthers()
    {
        var restored = await SeedExpiredFileAsync("restored.pdf");
        var purged = await SeedExpiredFileAsync("purged.pdf");

        // Alguien toca el primero justo antes de que la purga lo guarde: su RowVersion ya no coincide.
        await PurgeOnceAsync(new TouchBeforeSaving(fixture.ConnectionString, restored));

        await using var check = fixture.NewContext(_tenantId);
        Assert.True(await check.Files.IgnoreQueryFilters().AnyAsync(f => f.Id == restored));
        Assert.False(await check.Files.IgnoreQueryFilters().AnyAsync(f => f.Id == purged));
    }

    private async Task PurgeOnceAsync(IInterceptor interceptor)
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddScoped<ITenantContext, MutableTenant>()
            .AddScoped(sp => new CloudStorageDbContext(
                new DbContextOptionsBuilder<CloudStorageDbContext>()
                    .UseSqlServer(fixture.ConnectionString)
                    .AddInterceptors(interceptor)
                    .Options,
                sp.GetRequiredService<ITenantContext>()
            ))
            .AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CloudStorageDbContext>())
            .AddScoped<IFileObjectRepository, FileObjectRepository>()
            .AddScoped<IFolderRepository, FolderRepository>()
            .AddScoped<IStorageLimitRepository, StorageLimitRepository>()
            .AddScoped<IStorageAuditRepository, StorageAuditRepository>()
            .AddSingleton<IObjectStorage>(new FakeObjectStorage())
            .AddSingleton<IMultipartUploadStorage>(new FakeMultipartUploadStorage())
            .AddSingleton<ISystemClock>(new FakeSystemClock(DateTime.UtcNow))
            .AddSingleton(Options.Create(new CloudStorageOptions()))
            .BuildServiceProvider();

        var job = new RecycleBinPurgeService(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<RecycleBinPurgeService>.Instance
        );
        var purge = typeof(RecycleBinPurgeService).GetMethod(
            "PurgeAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)purge.Invoke(job, [CancellationToken.None])!;
    }

    private async Task<Guid> SeedExpiredFileAsync(string name)
    {
        var fileId = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{fileId:N}.pdf").Value;
        var file = FileObject
            .Register(
                fileId,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                name,
                "application/pdf",
                FileSize,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
        Assert.True(file.MarkPendingScan(now).IsSuccess);
        Assert.True(file.MarkScanning().IsSuccess);
        Assert.True(
            file.MarkAvailable(ChecksumSha256.Create(new string('d', 64)).Value, "application/pdf", now).IsSuccess
        );
        Assert.True(file.SoftDelete(now.AddDays(-31), TimeSpan.FromDays(30)).IsSuccess);

        await using var db = fixture.NewContext(_tenantId);
        if (!await db.StorageLimits.IgnoreQueryFilters().AnyAsync(l => l.TenantId == _tenantId))
            db.StorageLimits.Add(TenantStorageLimit.Create(_tenantId, "starter", 100_000_000, 100_000_000));
        db.Files.Add(file);
        await db.SaveChangesAsync();
        return fileId;
    }

    private sealed class MutableTenant : ITenantContext
    {
        public Guid TenantId { get; private set; }
        public bool HasTenant => TenantId != Guid.Empty;

        public void SetTenant(Guid id) => TenantId = id;
    }

    // Cambia la fila desde otra conexión la primera vez que se va a borrar ese archivo.
    private sealed class TouchBeforeSaving(string connectionString, Guid fileId) : SaveChangesInterceptor
    {
        private int _touched;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var deletesTarget = eventData
                .Context!.ChangeTracker.Entries<FileObject>()
                .Any(entry => entry.State == EntityState.Deleted && entry.Entity.Id == fileId);
            if (deletesTarget && Interlocked.Exchange(ref _touched, 1) == 0)
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync(cancellationToken);
                await using var command = new SqlCommand(
                    "UPDATE [Files] SET [OriginalName] = [OriginalName] WHERE [Id] = @id",
                    connection
                );
                command.Parameters.AddWithValue("@id", fileId);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }
}
