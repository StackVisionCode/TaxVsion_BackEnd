using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Retention;

namespace TaxVision.Notification.Tests.Retention;

public sealed class NotificationLogRetentionPurgerTests
{
    [Fact]
    public async Task PurgeAsync_deletes_old_logs_for_all_tenants_and_their_attempts_in_batches()
    {
        var databaseName = Guid.NewGuid().ToString("N");
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var oldA = CreateLog(tenantA, "old-a@example.com", DateTime.UtcNow.AddMonths(-13));
        var oldB = CreateLog(tenantB, "old-b@example.com", DateTime.UtcNow.AddMonths(-14));
        var recent = CreateLog(tenantA, "recent@example.com", DateTime.UtcNow.AddMonths(-1));
        oldA.AddDispatchAttempt(NotificationChannel.Email);
        oldB.AddDispatchAttempt(NotificationChannel.Email);

        await using (var seed = CreateContext(databaseName, new EmptyTenantContext()))
        {
            await seed.NotificationLogs.AddRangeAsync(oldA, oldB, recent);
            await seed.SaveChangesAsync();
        }

        await using (var db = CreateContext(databaseName, new EmptyTenantContext()))
        {
            var purger = new NotificationLogRetentionPurger(db);

            var purged = await purger.PurgeAsync(DateTime.UtcNow.AddMonths(-12), 1, CancellationToken.None);

            Assert.Equal(2, purged);
        }

        await using (var verify = CreateContext(databaseName, new EmptyTenantContext()))
        {
            var remainingLogs = await verify.NotificationLogs.IgnoreQueryFilters().ToListAsync();
            var remainingAttempts = await verify.NotificationDispatchAttempts.IgnoreQueryFilters().ToListAsync();
            Assert.Equal(recent.Id, Assert.Single(remainingLogs).Id);
            Assert.Empty(remainingAttempts);
        }
    }

    private static NotificationLog CreateLog(Guid tenantId, string recipient, DateTime createdAtUtc)
    {
        var result = NotificationLog.Create(
            tenantId,
            NotificationChannel.Email,
            recipient,
            "Subject",
            "template",
            null,
            null
        );
        Assert.True(result.IsSuccess);
        var log = result.Value;
        typeof(NotificationLog).GetProperty(nameof(NotificationLog.CreatedAtUtc))!.SetValue(log, createdAtUtc);
        return log;
    }

    private static NotificationDbContext CreateContext(string name, ITenantContext tenantContext) =>
        new(new DbContextOptionsBuilder<NotificationDbContext>().UseInMemoryDatabase(name).Options, tenantContext);

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) => throw new NotSupportedException();
    }
}
