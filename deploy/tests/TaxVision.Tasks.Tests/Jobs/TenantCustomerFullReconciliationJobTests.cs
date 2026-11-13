using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Web.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Customers.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Infrastructure.Customers;
using TaxVision.Tasks.Infrastructure.Jobs;
using Xunit;

namespace TaxVision.Tasks.Tests.Jobs;

/// <summary>
/// La reconciliación recorre los clientes de TODAS las oficinas. Con un solo scope, el DbContext seguía todas las
/// entidades hasta el final (SaveChanges no las suelta): la memoria crecía con el total de la plataforma y cada
/// guardado revisaba todo lo acumulado. Ahora cada página tiene su propio scope.
/// </summary>
public sealed class TenantCustomerFullReconciliationJobTests
{
    [Fact]
    public async Task Each_page_runs_in_its_own_scope_so_tracked_entities_are_released()
    {
        var pages = new PagedClient(pageCount: 3, pageSize: 2);
        var seen = new List<FakeDirectory>();
        var saves = new List<FakeUnitOfWork>();
        var services = new ServiceCollection()
            .AddSingleton<IOptions<CustomerClientOptions>>(
                Options.Create(
                    new CustomerClientOptions { ReconciliationPageSize = 2, ReconciliationIntervalHours = 1 }
                )
            )
            .AddSingleton<IHostApplicationLifetime>(new StartedLifetime())
            .AddSingleton<ITasksCustomerClient>(pages)
            .AddScoped<ICustomerDirectoryRepository>(_ =>
            {
                var directory = new FakeDirectory();
                seen.Add(directory);
                return directory;
            })
            .AddScoped<IUnitOfWork>(_ =>
            {
                var unit = new FakeUnitOfWork();
                saves.Add(unit);
                return unit;
            })
            .AddScoped<ICorrelationContext, CorrelationContext>()
            .AddSingleton<ILogger<TenantCustomerFullReconciliationJob>>(
                NullLogger<TenantCustomerFullReconciliationJob>.Instance
            )
            .BuildServiceProvider();
        var job = new TenantCustomerFullReconciliationJob(
            services,
            services.GetRequiredService<ILogger<TenantCustomerFullReconciliationJob>>()
        );

        await job.StartAsync(CancellationToken.None);
        await pages.LastPageServed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await job.StopAsync(CancellationToken.None);

        var used = seen.Where(directory => directory.Added > 0).ToList();
        Assert.Equal(3, used.Count);
        Assert.All(used, directory => Assert.Equal(2, directory.Added));
        Assert.Equal(3, saves.Count(unit => unit.Saves == 1));
    }

    private sealed class PagedClient(int pageCount, int pageSize) : ITasksCustomerClient
    {
        public TaskCompletionSource LastPageServed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<PagedResult<RemoteCustomerSummary>?>(null);

        public Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            var items = Enumerable
                .Range(0, pageSize)
                .Select(i => new RemoteReconciliationCustomer(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    $"Customer {page}-{i}",
                    CustomerDirectoryStatus.Active
                ))
                .ToList();
            if (page == pageCount)
                LastPageServed.TrySetResult();
            return Task.FromResult<RemoteCustomerReconciliationPage?>(new(items, HasMore: page < pageCount));
        }
    }

    private sealed class FakeDirectory : ICustomerDirectoryRepository
    {
        public int Added { get; private set; }

        public Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(
            Guid tenantId,
            Guid customerId,
            CancellationToken ct = default
        ) => Task.FromResult<CustomerDirectoryEntry?>(null);

        public Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default)
        {
            Added++;
            return Task.CompletedTask;
        }

        public Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task UpsertBulkAsync(
            Guid tenantId,
            IReadOnlyCollection<Guid> customerIds,
            DateTime observedAtUtc,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(
            int limit,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task ApplyDisplayNameIfMissingAsync(
            Guid tenantId,
            Guid customerId,
            string displayName,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public int Saves { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            Saves++;
            return Task.FromResult(1);
        }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted { get; } = new(canceled: true);
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }
}
