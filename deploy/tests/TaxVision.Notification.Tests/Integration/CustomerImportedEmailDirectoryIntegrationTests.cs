using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Directory.Consumers;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>NOT-21: un importado llega solo con CustomersBulkImported, que no trae correos.</summary>
public sealed class CustomerImportedEmailDirectoryIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn21_");
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly PagedCustomerClient _client = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CustomerContactPolicyConsumer));
                options.Discovery.IncludeType(typeof(CustomerEmailDirectoryConsumer));
                options.Discovery.IncludeType(typeof(SyncImportedCustomerEmailsHandler));
                options.UseTaxVisionDurableLocalQueues();
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_database.ConnectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, NotificationDbContext>();
                options.Policies.AutoApplyTransactions();
                options.Services.AddSingleton<ITenantContext, NotificationTempDatabase.NoTenant>();
                options.Services.AddDbContext<NotificationDbContext>(db => db.UseSqlServer(_database.ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
                options.Services.AddScoped<ICustomerContactPolicyRepository, CustomerContactPolicyRepository>();
                options.Services.AddScoped<ICustomerEmailDirectoryRepository, CustomerEmailDirectoryRepository>();
                options.Services.AddSingleton<INotificationCustomerClient>(_client);
            })
            .StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DisposeAsync();
    }

    private CustomersBulkImportedIntegrationEvent Imported(Guid[] created, Guid[] updated) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "not-21",
            ImportJobId = Guid.NewGuid(),
            CreatedByUserId = Guid.NewGuid(),
            CompletedAtUtc = DateTime.UtcNow,
            TotalRows = created.Length + updated.Length,
            SuccessCount = created.Length,
            UpdatedCount = updated.Length,
            SkippedCount = 0,
            FailedCount = 0,
            CreatedCustomerIds = created,
            UpdatedCustomerIds = updated,
        };

    private async Task<string?> EmailOfAsync(Guid customerId)
    {
        await using var db = _database.NewContext();
        return await db
            .CustomerEmailDirectoryEntries.IgnoreQueryFilters()
            .Where(x => x.TenantId == _tenant && x.CustomerId == customerId)
            .Select(x => x.NormalizedEmail)
            .FirstOrDefaultAsync();
    }

    [Fact]
    public async Task Imported_customers_get_their_directory_row_without_waiting_for_the_reconciliation()
    {
        var created = Guid.NewGuid();
        var updated = Guid.NewGuid();
        _client.Add(_tenant, created, "created@not21.test");
        _client.Add(_tenant, updated, "updated@not21.test");
        _client.Add(_tenant, Guid.NewGuid(), "other@not21.test");

        await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(Imported([created], [updated]));

        // La fila llega por una cola local, después del evento.
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline && await EmailOfAsync(updated) is null)
            await Task.Delay(100);

        Assert.Equal("created@not21.test", await EmailOfAsync(created));
        Assert.Equal("updated@not21.test", await EmailOfAsync(updated));
    }

    private sealed class PagedCustomerClient : INotificationCustomerClient
    {
        private readonly List<RemoteCustomerContact> _contacts = [];

        public void Add(Guid tenantId, Guid customerId, string email) =>
            _contacts.Add(new RemoteCustomerContact(tenantId, customerId, "Name", email, true));

        public Task<RemoteCustomerPage?> ListAllForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<RemoteCustomerPage?>(new RemoteCustomerPage(_contacts, false));

        // Una fila por página: obliga a seguir hasta encontrarlos a todos.
        public Task<RemoteCustomerPage?> ListTenantCustomersAsync(
            Guid tenantId,
            int page,
            CancellationToken ct = default
        )
        {
            var ofTenant = _contacts.Where(x => x.TenantId == tenantId).ToList();
            return Task.FromResult<RemoteCustomerPage?>(
                new RemoteCustomerPage(ofTenant.Skip(page - 1).Take(1).ToList(), page < ofTenant.Count)
            );
        }
    }
}
