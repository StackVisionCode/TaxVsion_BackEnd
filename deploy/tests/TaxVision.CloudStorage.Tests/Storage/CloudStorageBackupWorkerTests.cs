using System.Reflection;
using BuildingBlocks.RateLimiting;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.CloudStorage.Domain.Providers;
using TaxVision.CloudStorage.Infrastructure.Persistence;
using TaxVision.CloudStorage.Infrastructure.Storage;

namespace TaxVision.CloudStorage.Tests.Storage;

// Oficina sin el módulo: su backup no toma el candado ni queda marcado como hecho. Sin proyección todavía, sigue.
public sealed class CloudStorageBackupWorkerTests
{
    [Fact]
    public async Task An_office_without_the_module_is_skipped_before_the_lock()
    {
        var suspended = Guid.NewGuid();
        var withoutProjection = Guid.NewGuid();
        await using var db = new CloudStorageDbContext(
            new DbContextOptionsBuilder<CloudStorageDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new NoTenantContext()
        );
        db.StoragePolicies.AddRange(DailyBackup(suspended), DailyBackup(withoutProjection));
        await db.SaveChangesAsync();
        var lockRequests = 0;
        var services = new ServiceCollection()
            .AddSingleton(db)
            .AddScoped(sp =>
            {
                lockRequests++;
                return new SqlTenantBackupLock(sp.GetRequiredService<CloudStorageDbContext>());
            })
            .AddSingleton<ITenantEntitlementModulesReader>(new ModulesByTenant(new() { [suspended] = ["wallet"] }))
            .BuildServiceProvider();
        var worker = new CloudStorageBackupWorker(
            services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<CloudStorageBackupWorker>.Instance
        );

        var run = typeof(CloudStorageBackupWorker).GetMethod(
            "RunDueBackupsAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        await (Task)run.Invoke(worker, [CancellationToken.None])!;

        // Solo la oficina sin proyección llega al candado.
        Assert.Equal(1, lockRequests);
        var policy = await db.StoragePolicies.IgnoreQueryFilters().SingleAsync(x => x.TenantId == suspended);
        Assert.Null(policy.LastBackupAtUtc);
    }

    private static TenantStoragePolicy DailyBackup(Guid tenantId)
    {
        var policy = TenantStoragePolicy.Create(tenantId, "google-drive");
        policy.Configure("google-drive", "backup", "daily", "00:00", "America/New_York");
        return policy;
    }

    // Sin fila para el tenant = sin proyección (null).
    private sealed class ModulesByTenant(Dictionary<Guid, IReadOnlyList<string>> modules)
        : ITenantEntitlementModulesReader
    {
        public Task<IReadOnlyList<string>?> GetEnabledModulesAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(modules.GetValueOrDefault(tenantId));
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException();
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
