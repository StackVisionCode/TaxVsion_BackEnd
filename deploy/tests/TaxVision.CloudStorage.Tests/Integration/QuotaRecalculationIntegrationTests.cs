using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;
using TaxVision.CloudStorage.Tests.Application;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>
/// CST-5 contra el SQL Server real: el recálculo lee la fila con candado y suma desde Files con la misma
/// regla que QuotaFootprint (papelera y bloqueo DMCA incluidos). Tenant propio; se borra al terminar.
/// </summary>
public sealed class QuotaRecalculationIntegrationTests : IAsyncLifetime
{
    private const string Sha256 = "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee";
    private readonly Guid _tenantId = Guid.NewGuid();

    private readonly string _connectionString =
        Environment.GetEnvironmentVariable("CLOUDSTORAGE_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("taxvision-cloudstorage-development")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException(
            "Set CLOUDSTORAGE_DB_CONNECTION or the CloudStorage user-secrets to run this test."
        );

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CloudStorageDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
        );

    public async Task InitializeAsync()
    {
        var now = DateTime.UtcNow;
        var available = Available(100, now);
        var dmca = Available(40, now);
        dmca.BlockForTakedown(now);
        var deletedAvailable = Available(30, now);
        deletedAvailable.SoftDelete(now, TimeSpan.FromDays(30));
        var infected = Scanning(50, now);
        infected.MarkInfected("Eicar", "application/pdf", now);
        var deletedInfected = Scanning(60, now);
        deletedInfected.MarkInfected("Eicar", "application/pdf", now);
        deletedInfected.SoftDelete(now, TimeSpan.FromDays(30));
        var pending = Register(20, now);

        var limit = TenantStorageLimit.Create(_tenantId, "starter", 1_000_000, 1_000_000);
        limit.Reserve(900);
        limit.Commit(700);

        await using var db = NewContext();
        db.StorageLimits.Add(limit);
        db.Files.AddRange(available, dmca, deletedAvailable, infected, deletedInfected, pending);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.AccessLogs.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.StorageLimits.IgnoreQueryFilters().Where(l => l.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task Recalculate_SetsCountersFromFilesUnderLock()
    {
        await using (var db = NewContext())
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var result = await RecalculateTenantQuotaHandler.Handle(
                new RecalculateTenantQuotaCommand(_tenantId, Guid.NewGuid()),
                new StorageLimitRepository(db),
                new FileObjectRepository(db),
                new StorageAuditRepository(db),
                new FakeSystemClock(DateTime.UtcNow),
                db,
                CancellationToken.None
            );
            await transaction.CommitAsync();

            Assert.True(result.IsSuccess);
            Assert.Equal(700, result.Value.UsedBytesBefore);
            Assert.Equal(200, result.Value.ReservedBytesBefore);
        }

        await using var read = NewContext();
        var limit = await read
            .StorageLimits.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(l => l.TenantId == _tenantId);
        Assert.Equal(170, limit.UsedBytes); // 100 Available + 40 DMCA + 30 en papelera desde Available
        Assert.Equal(20, limit.ReservedBytes);
        Assert.Equal(
            1,
            await read
                .AccessLogs.IgnoreQueryFilters()
                .CountAsync(l => l.TenantId == _tenantId && l.Action == "quota.recalculated")
        );
    }

    private FileObject Register(long size, DateTime now)
    {
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{Guid.NewGuid():N}.pdf").Value;
        return FileObject
            .Register(
                Guid.NewGuid(),
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "quota.pdf",
                "application/pdf",
                size,
                Guid.NewGuid(),
                now,
                now.AddHours(1)
            )
            .Value;
    }

    private FileObject Scanning(long size, DateTime now)
    {
        var file = Register(size, now);
        file.MarkPendingScan(now);
        file.MarkScanning();
        return file;
    }

    private FileObject Available(long size, DateTime now)
    {
        var file = Scanning(size, now);
        file.MarkAvailable(ChecksumSha256.Create(Sha256).Value, "application/pdf", now);
        return file;
    }
}
