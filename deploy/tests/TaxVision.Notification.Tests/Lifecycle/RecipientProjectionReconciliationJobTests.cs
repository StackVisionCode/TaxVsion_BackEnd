using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using TaxVision.Notification.Infrastructure.Reconciliation;

namespace TaxVision.Notification.Tests.Lifecycle;

/// <summary>La reconciliación repara el directorio de correos con lo que dice Auth.</summary>
public sealed class RecipientProjectionReconciliationJobTests
{
    private readonly string _database = Guid.NewGuid().ToString();
    private readonly FakeSnapshotClient _auth = new();

    [Fact]
    public async Task Inactive_row_is_reactivated_when_auth_says_active()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        await SeedAsync(UserEmailDirectoryEntry.Create(tenantId, userId, "user@example.test", isActive: false));
        _auth.Contacts[(tenantId, userId)] = new RemoteUserContact("user@example.test", IsActive: true);

        await NewJob().ReconcileUserEmailDirectoryAsync(pageSize: 1, CancellationToken.None);

        var entry = await ReadAsync(tenantId, userId);
        Assert.True(entry.IsActive);
        Assert.NotNull(entry.LastSourceEventAtUtc);
    }

    [Fact]
    public async Task Auth_failure_for_one_user_does_not_stop_the_rest()
    {
        var tenantId = Guid.NewGuid();
        var unknown = Guid.NewGuid();
        var stale = Guid.NewGuid();
        await SeedAsync(UserEmailDirectoryEntry.Create(tenantId, unknown, "gone@example.test", isActive: false));
        var staleEntry = UserEmailDirectoryEntry.Create(tenantId, stale, "old@example.test");
        staleEntry.MarkStale(DateTime.UtcNow.AddDays(-1));
        await SeedAsync(staleEntry);
        _auth.Contacts[(tenantId, stale)] = new RemoteUserContact("new@example.test", IsActive: true);

        await NewJob().ReconcileUserEmailDirectoryAsync(pageSize: 1, CancellationToken.None);

        var repaired = await ReadAsync(tenantId, stale);
        Assert.False(repaired.IsStale);
        Assert.Equal("new@example.test", repaired.Email);
        Assert.False((await ReadAsync(tenantId, unknown)).IsActive);
    }

    private RecipientProjectionReconciliationJob NewJob()
    {
        var services = new ServiceCollection();
        services.AddDbContext<NotificationDbContext>(o => o.UseInMemoryDatabase(_database));
        services.AddSingleton<ITenantContext, NoTenant>();
        services.AddScoped<IUnitOfWork>(p => p.GetRequiredService<NotificationDbContext>());
        services.AddScoped<IUserEmailDirectoryRepository, UserEmailDirectoryRepository>();
        services.AddSingleton<IUserContactSnapshotClient>(_auth);
        return new RecipientProjectionReconciliationJob(
            services.BuildServiceProvider(),
            NullLogger<RecipientProjectionReconciliationJob>.Instance
        );
    }

    private NotificationDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseInMemoryDatabase(_database).Options,
            new NoTenant()
        );

    private async Task SeedAsync(UserEmailDirectoryEntry entry)
    {
        await using var db = NewContext();
        db.UserEmailDirectoryEntries.Add(entry);
        await db.SaveChangesAsync();
    }

    private async Task<UserEmailDirectoryEntry> ReadAsync(Guid tenantId, Guid userId)
    {
        await using var db = NewContext();
        return await db
            .UserEmailDirectoryEntries.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(e => e.TenantId == tenantId && e.UserId == userId);
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private sealed class FakeSnapshotClient : IUserContactSnapshotClient
    {
        public Dictionary<(Guid, Guid), RemoteUserContact> Contacts { get; } = [];

        public Task<RemoteUserContact?> FetchContactAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Contacts.GetValueOrDefault((tenantId, userId)));
    }
}
