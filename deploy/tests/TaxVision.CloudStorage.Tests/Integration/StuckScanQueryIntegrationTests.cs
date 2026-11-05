using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

namespace TaxVision.CloudStorage.Tests.Integration;

/// <summary>Contra el SQL Server real: la consulta del rescate mide desde ScanQueuedAtUtc o, si falta, desde CreatedAtUtc.</summary>
public sealed class StuckScanQueryIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;
    private Guid _oldQueued;
    private Guid _recentQueued;
    private Guid _oldButRequeued;

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
        var oldQueued = NewFile(_now.AddHours(-5));
        oldQueued.MarkPendingScan(_now.AddHours(-5));
        var recentQueued = NewFile(_now.AddHours(-5));
        recentQueued.MarkPendingScan(_now.AddMinutes(-10));
        var oldButRequeued = NewFile(_now.AddHours(-6));
        oldButRequeued.MarkPendingScan(_now.AddHours(-6));
        oldButRequeued.RequeueScan(_now.AddMinutes(-5), maxAttempts: 3);
        (_oldQueued, _recentQueued, _oldButRequeued) = (oldQueued.Id, recentQueued.Id, oldButRequeued.Id);

        await using var db = NewContext();
        db.Files.AddRange(oldQueued, recentQueued, oldButRequeued);
        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Files.IgnoreQueryFilters().Where(f => f.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    [Fact]
    public async Task ListStuckScans_UsesQueuedTimeAndSkipsRecentOnes()
    {
        await using var db = NewContext();
        var stuck = await new FileObjectRepository(db).ListStuckScansAsync(_now.AddHours(-2), 1000, CancellationToken.None);

        var mine = stuck.Where(f => f.TenantId == _tenantId).Select(f => f.Id).ToList();
        Assert.Contains(_oldQueued, mine);
        Assert.DoesNotContain(_recentQueued, mine);
        Assert.DoesNotContain(_oldButRequeued, mine);
    }

    private FileObject NewFile(DateTime createdAtUtc)
    {
        var id = Guid.NewGuid();
        var key = ObjectKey.Create($"tenants/{_tenantId:N}/tenant/documents/2026/{id:N}.pdf").Value;
        return FileObject
            .Register(
                id,
                _tenantId,
                OwnerType.Tenant,
                null,
                FolderType.Documents,
                2026,
                key,
                "stuck.pdf",
                "application/pdf",
                10,
                Guid.NewGuid(),
                createdAtUtc,
                createdAtUtc.AddHours(1)
            )
            .Value;
    }
}
