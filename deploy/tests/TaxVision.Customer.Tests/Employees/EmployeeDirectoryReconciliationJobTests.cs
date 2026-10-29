using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Employees;
using TaxVision.Customer.Infrastructure.Employees;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;

namespace TaxVision.Customer.Tests.Employees;

/// <summary>
/// La pasada completa contra la persistencia real de EF: recorre todas las páginas de Auth sin tenant en
/// contexto (es cross-tenant) y deja el directorio igual que el staff de Auth. Si Auth no responde, no
/// toca nada.
/// </summary>
public sealed class EmployeeDirectoryReconciliationJobTests
{
    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) => throw new InvalidOperationException("The job runs without a tenant.");
    }

    private sealed class PagedAuth(params RemoteStaffPage?[] pages) : IAuthStaffDirectoryClient
    {
        public List<int> RequestedPages { get; } = [];

        public Task<RemoteStaffPage?> ListStaffAsync(int page, int size, CancellationToken ct = default)
        {
            RequestedPages.Add(page);
            return Task.FromResult(page <= pages.Length ? pages[page - 1] : null);
        }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => new(canceled: true);
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    private static readonly Guid TenantA = Guid.NewGuid();
    private static readonly Guid TenantB = Guid.NewGuid();

    private static ServiceProvider Services(string databaseName, IAuthStaffDirectoryClient auth) =>
        new ServiceCollection()
            .AddScoped<ITenantContext, NoTenantContext>()
            .AddScoped(sp => new CustomerDbContext(
                new DbContextOptionsBuilder<CustomerDbContext>().UseInMemoryDatabase(databaseName).Options,
                sp.GetRequiredService<ITenantContext>()
            ))
            .AddScoped<ITenantEmployeeDirectoryReconciliationRepository, TenantEmployeeDirectoryRepository>()
            .AddSingleton(auth)
            .BuildServiceProvider();

    private static EmployeeDirectoryReconciliationJob Job(IServiceProvider services) =>
        new(
            services.GetRequiredService<IServiceScopeFactory>(),
            new StartedLifetime(),
            Options.Create(new EmployeeDirectoryReconciliationOptions { PageSize = 2 }),
            NullLogger<EmployeeDirectoryReconciliationJob>.Instance
        );

    private static async Task<List<TenantEmployeeDirectoryEntry>> DirectoryAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<CustomerDbContext>()
            .TenantEmployeeDirectoryEntries.ToListAsync();
    }

    [Fact]
    public async Task Walks_every_page_and_leaves_the_directory_like_Auth()
    {
        var databaseName = Guid.NewGuid().ToString();
        var known = TenantEmployeeDirectoryEntry.Create(Guid.NewGuid(), TenantA, "PlatformAdmin", isActive: true);
        var owner = new RemoteStaffUser(TenantA, Guid.NewGuid(), "TenantAdmin", IsActive: true, IsOffboarded: false);
        var employee = new RemoteStaffUser(
            TenantB,
            Guid.NewGuid(),
            "TenantEmployee",
            IsActive: true,
            IsOffboarded: false
        );
        var auth = new PagedAuth(
            new RemoteStaffPage(
                [owner, new RemoteStaffUser(TenantA, known.UserId, "TenantAdmin", true, false)],
                HasMore: true
            ),
            new RemoteStaffPage([employee], HasMore: false)
        );
        await using var services = Services(databaseName, auth);
        await using (var seed = services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<CustomerDbContext>();
            db.TenantEmployeeDirectoryEntries.Add(known);
            await db.SaveChangesAsync();
        }

        await Job(services).RunOnceAsync(CancellationToken.None);

        var directory = (await DirectoryAsync(services)).ToDictionary(entry => entry.UserId);
        Assert.Equal([1, 2], auth.RequestedPages);
        Assert.Equal(3, directory.Count);
        Assert.Equal("TenantAdmin", directory[owner.UserId].ActorType);
        Assert.Equal("TenantAdmin", directory[known.UserId].ActorType);
        Assert.Equal(TenantB, directory[employee.UserId].TenantId);
    }

    [Fact]
    public async Task Touches_nothing_when_Auth_does_not_answer()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var services = Services(databaseName, new PagedAuth(pages: [null]));

        await Job(services).RunOnceAsync(CancellationToken.None);

        Assert.Empty(await DirectoryAsync(services));
    }
}
