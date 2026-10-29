using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Avatars;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Tests.Application;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Persistence;

public sealed class CustomerAvatarPendingSweeperTests
{
    private static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
    private readonly string _database = Guid.NewGuid().ToString();

    [Fact]
    public async Task Discards_only_pending_pictures_older_than_the_timeout_across_tenants()
    {
        var expiredA = Pending(Guid.NewGuid(), Now.AddMinutes(-11));
        var expiredB = Pending(Guid.NewGuid(), Now.AddHours(-3));
        var fresh = Pending(Guid.NewGuid(), Now.AddMinutes(-2));
        var current = Guid.NewGuid();
        expiredA.BeginProfilePictureChange(current, Guid.NewGuid(), Now.AddDays(-1));
        expiredA.ConfirmProfilePicture(current);
        expiredA.BeginProfilePictureChange(Guid.NewGuid(), Guid.NewGuid(), Now.AddMinutes(-11));

        await using (var seed = CreateContext())
        {
            seed.Customers.AddRange(expiredA, expiredB, fresh);
            await seed.SaveChangesAsync();
        }

        int discarded;
        await using (var db = CreateContext())
            discarded = await new CustomerAvatarPendingSweeper(db, new FixedTimeProvider(Now)).SweepOnceAsync(
                CancellationToken.None
            );

        Assert.Equal(2, discarded);
        await using var check = CreateContext();
        var rows = await check.Customers.IgnoreQueryFilters().ToDictionaryAsync(c => c.Id);
        Assert.Null(rows[expiredA.Id].PendingProfilePictureFileId);
        Assert.Equal(current, rows[expiredA.Id].ProfilePictureFileId);
        Assert.Null(rows[expiredB.Id].PendingProfilePictureFileId);
        Assert.NotNull(rows[fresh.Id].PendingProfilePictureFileId);
    }

    [Fact]
    public async Task Nothing_to_sweep_returns_zero()
    {
        await using var db = CreateContext();

        var discarded = await new CustomerAvatarPendingSweeper(db, new FixedTimeProvider(Now)).SweepOnceAsync(
            CancellationToken.None
        );

        Assert.Equal(0, discarded);
    }

    private static DomainCustomer Pending(Guid tenantId, DateTime requestedAt)
    {
        var customer = DomainCustomer
            .Register(
                tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Ana", "Client").Value,
                null,
                EmailAddress.Create($"ana-{Guid.NewGuid():N}@example.com").Value,
                null,
                Language.En,
                Guid.NewGuid()
            )
            .Value;
        customer.BeginProfilePictureChange(Guid.NewGuid(), Guid.NewGuid(), requestedAt);
        return customer;
    }

    private CustomerDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(_database).Options, new NoTenant());

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
