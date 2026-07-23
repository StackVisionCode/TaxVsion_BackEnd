using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

namespace TaxVision.Correspondence.Tests.Persistence;

/// <summary>
/// <see cref="EmailThreadRepository.ListByCustomerAsync"/> contra el repositorio EF real (no el
/// fake): cubre paginación con más filas de las que entran en una página, para atrapar off-by-one
/// que un fake in-memory con LINQ podría esconder distinto que SQL Server real.
/// </summary>
public sealed class EmailThreadRepositoryTests
{
    // RBAC Fase 5 — EmailThread ahora es ITenantOwned; se setea el tenant "propio" de cada test
    // antes de consultar, igual que haría JwtTenantContextMiddleware en producción.
    private sealed class FakeTenantContext : ITenantContext
    {
        private Guid? _tenantId;
        public Guid TenantId => _tenantId ?? throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => _tenantId.HasValue;

        public void SetTenant(Guid tenantId) => _tenantId = tenantId;
    }

    private static CorrespondenceDbContext CreateContext(ITenantContext tenantContext) =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenantContext
        );

    [Fact]
    public async Task ListByCustomerAsync_WithFiveThreads_PaginatesInLastMessageAtUtcDescendingOrder()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new EmailThreadRepository(db);
        var customerId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var threads = new List<EmailThread>();
        for (var i = 0; i < 5; i++)
        {
            var thread = EmailThread
                .NewFromMessage(tenantId, customerId, $"Subject {i}", null, now.AddMinutes(i))
                .Value;
            threads.Add(thread);
            await repository.AddAsync(thread);
        }
        await db.SaveChangesAsync();

        var page1 = await repository.ListByCustomerAsync(tenantId, customerId, page: 1, size: 2);
        var page2 = await repository.ListByCustomerAsync(tenantId, customerId, page: 2, size: 2);
        var page3 = await repository.ListByCustomerAsync(tenantId, customerId, page: 3, size: 2);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal([threads[4].Id, threads[3].Id], page1.Items.Select(x => x.Id));
        Assert.Equal([threads[2].Id, threads[1].Id], page2.Items.Select(x => x.Id));
        Assert.Equal([threads[0].Id], page3.Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ListByCustomerAsync_WithZeroOrNegativePageAndSize_ClampsToDefaults()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new EmailThreadRepository(db);
        var customerId = Guid.NewGuid();
        var thread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        await repository.AddAsync(thread);
        await db.SaveChangesAsync();

        var result = await repository.ListByCustomerAsync(tenantId, customerId, page: 0, size: -5);

        Assert.Equal(1, result.Page);
        Assert.Equal(20, result.Size);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task ListByCustomerAsync_WithSizeAboveMax_ClampsToMaxPageSize()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new EmailThreadRepository(db);
        var customerId = Guid.NewGuid();

        var result = await repository.ListByCustomerAsync(tenantId, customerId, page: 1, size: 500);

        Assert.Equal(100, result.Size);
    }

    [Fact]
    public async Task ListByCustomerAsync_WithThreadFromAnotherCustomer_NeverReturnsIt()
    {
        var tenantId = Guid.NewGuid();
        var tenantContext = new FakeTenantContext();
        tenantContext.SetTenant(tenantId);
        await using var db = CreateContext(tenantContext);
        var repository = new EmailThreadRepository(db);
        var customerId = Guid.NewGuid();
        var ownThread = EmailThread.NewFromMessage(tenantId, customerId, "Subject", null, DateTime.UtcNow).Value;
        var otherCustomerThread = EmailThread
            .NewFromMessage(tenantId, Guid.NewGuid(), "Other subject", null, DateTime.UtcNow)
            .Value;
        await repository.AddAsync(ownThread);
        await repository.AddAsync(otherCustomerThread);
        await db.SaveChangesAsync();

        var result = await repository.ListByCustomerAsync(tenantId, customerId, page: 1, size: 20);

        var item = Assert.Single(result.Items);
        Assert.Equal(ownThread.Id, item.Id);
    }
}
