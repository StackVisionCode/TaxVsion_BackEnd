using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Infrastructure.Scheduling;
using TaxVision.Correspondence.Tests.Projections;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// Contra el SQL Server real (índice único filtrado de email activo): un email compartido en la página 1 no
/// corta la reconciliación de las páginas siguientes.
/// </summary>
public sealed class CustomerProjectionReconciliationIntegrationTests : IAsyncLifetime
{
    private readonly Guid _tenantId = Guid.NewGuid();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = CorrespondenceSqlServer.NewContext(_tenantId);
        await db.CustomerEmailAddresses.IgnoreQueryFilters().Where(x => x.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private sealed class PagedClient(params IReadOnlyList<RemoteCustomerRecord>[] pages) : ICustomerReconciliationClient
    {
        public Task<CustomerReconciliationPage?> ListPageAsync(int page, int size, CancellationToken ct = default) =>
            Task.FromResult<CustomerReconciliationPage?>(new(pages[page - 1], page < pages.Length));
    }

    [Fact]
    public async Task SharedEmailOnPage1_DoesNotBlockLaterPages()
    {
        var shared = $"shared-{Guid.NewGuid():N}@cor06.test";
        var owner = Guid.NewGuid();
        var conflicting = Guid.NewGuid();
        var page2Customer = Guid.NewGuid();
        var page3Customer = Guid.NewGuid();
        await using (var seed = CorrespondenceSqlServer.NewContext(_tenantId))
        {
            seed.CustomerEmailAddresses.Add(CustomerEmailAddress.Create(_tenantId, owner, EmailAddress.Create(shared).Value));
            var deleted = CustomerEmailAddress.Create(_tenantId, conflicting, EmailAddress.Create(shared).Value);
            deleted.SoftDelete(DateTime.UtcNow.AddDays(-1));
            seed.CustomerEmailAddresses.Add(deleted);
            await seed.SaveChangesAsync();
        }

        RemoteCustomerRecord Active(Guid id, string email) => new(_tenantId, id, "Customer", email, true);
        var client = new PagedClient(
            [Active(owner, shared), Active(conflicting, shared)],
            [Active(page2Customer, $"p2-{Guid.NewGuid():N}@cor06.test")],
            [Active(page3Customer, $"p3-{Guid.NewGuid():N}@cor06.test")]
        );
        var services = new ServiceCollection();
        services.AddScoped(_ => CorrespondenceSqlServer.NewContext(_tenantId));
        services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CorrespondenceDbContext>());
        services.AddScoped<ICustomerEmailAddressRepository, CustomerEmailAddressRepository>();
        services.AddSingleton<ICustomerReconciliationClient>(client);
        services.AddSingleton<ICorrelationContext, FakeCorrelationContext>();
        await using var provider = services.BuildServiceProvider();
        var job = new CustomerProjectionReconciliationJob(
            provider,
            NullLogger<CustomerProjectionReconciliationJob>.Instance
        );

        await job.RunOnceSafeAsync(pageSize: 2, CancellationToken.None);

        await using var read = CorrespondenceSqlServer.NewContext(_tenantId);
        var rows = await read.CustomerEmailAddresses.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.TenantId == _tenantId)
            .ToListAsync();
        Assert.Contains(rows, x => x.CustomerId == page2Customer && x.IsActive);
        Assert.Contains(rows, x => x.CustomerId == page3Customer && x.IsActive);
        Assert.False(rows.Single(x => x.CustomerId == conflicting).IsActive);
        Assert.True(rows.Single(x => x.CustomerId == owner).IsActive);
    }
}
