using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Calendar.Application.Backfill;
using TaxVision.Calendar.Application.Backfill.Abstractions;
using TaxVision.Calendar.Application.Customers.Abstractions;
using TaxVision.Calendar.Application.Projections.Abstractions;
using TaxVision.Calendar.Application.Projections.CustomerEvents;
using TaxVision.Calendar.Infrastructure.Persistence;
using TaxVision.Calendar.Infrastructure.Persistence.Repositories;
using TaxVision.Calendar.Tests.Application;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Xunit;

namespace TaxVision.Calendar.Tests.Integration;

/// <summary>
/// CAL-5 por el pipeline real: el consumer devuelve el comando en cascada, sale por la cola local durable y la paginación
/// corre fuera de toda transacción de EF.
/// </summary>
public sealed class CustomerBackfillIntegrationTests : IAsyncLifetime
{
    private const string EnvelopeSchema = "calbackfill";

    private readonly CalendarTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly TransactionWatchingClient _client = new();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await _database.CreateAsync();
        try
        {
            _host = await StartHostAsync();
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await _database.DropAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task BurstOfEvents_SeedsOnce_OutsideAnyTransaction()
    {
        var bus = _host!.Services.GetRequiredService<IMessageBus>();
        _client.Seed(new RemoteCustomerSummary(Guid.NewGuid(), "Ana", true));

        for (var i = 0; i < 5; i++)
            await bus.InvokeAsync(NewEvent());

        await WaitForStateAsync();
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(1, _client.ListCalls);
        Assert.False(_client.SawTransaction);
        await using var read = _database.NewContext();
        Assert.Equal(
            6,
            await read.CustomerDirectoryEntries.IgnoreQueryFilters().CountAsync(e => e.TenantId == _tenantId)
        );
    }

    private async Task WaitForStateAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            await using var read = _database.NewContext();
            if (await read.TenantBackfillStates.IgnoreQueryFilters().AnyAsync(s => s.TenantId == _tenantId))
                return;
            await Task.Delay(100);
        }

        Assert.Fail("The tenant was not marked as seeded.");
    }

    private CustomerUpdatedIntegrationEvent NewEvent() =>
        new()
        {
            TenantId = _tenantId,
            CorrelationId = "corr",
            CustomerId = Guid.NewGuid(),
            DisplayName = "Jane",
            PrimaryEmail = "jane@example.test",
            Language = "en",
            ModifiedByUserId = Guid.NewGuid(),
        };

    private async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
            // El temporizador de Wolverine puede registrar tras el Dispose: EventLog lanzaría y tumbaría el proceso.
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                options.ServiceName = "cal-5-backfill-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CustomerUpdatedConsumer))
                    .IncludeType(typeof(BackfillTenantCustomersHandler))
                    .IncludeType(typeof(SaveCustomerDirectoryPageHandler));
                options.PersistMessagesWithSqlServer(_database.ConnectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                options.Services.AddDbContext<CalendarDbContext>(db => db.UseSqlServer(_database.ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CalendarDbContext>());
                options.Services.AddSingleton<ITenantContext, CalendarTemporaryDatabase.NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, NoCorrelation>();
                options.Services.AddScoped<ICustomerDirectoryRepository, CustomerDirectoryRepository>();
                options.Services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
                options.Services.AddScoped<ICalendarCustomerClient>(sp =>
                {
                    _client.Context = sp.GetRequiredService<CalendarDbContext>();
                    return _client;
                });
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CalendarDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDurableLocalQueues();
                options.LocalQueueFor<BackfillTenantCustomersCommand>().Sequential();
            })
            .StartAsync();

    /// <summary>Mira si hay una transacción de EF abierta en el momento de la llamada a Customer.</summary>
    private sealed class TransactionWatchingClient : ICalendarCustomerClient
    {
        private readonly List<RemoteCustomerSummary> _customers = [];

        public CalendarDbContext? Context { get; set; }

        public int ListCalls { get; private set; }

        public bool SawTransaction { get; private set; }

        public void Seed(RemoteCustomerSummary customer) => _customers.Add(customer);

        public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
            Guid tenantId,
            int page,
            int size,
            CancellationToken ct = default
        )
        {
            ListCalls++;
            SawTransaction |= Context?.Database.CurrentTransaction is not null;
            return Task.FromResult<PagedResult<RemoteCustomerSummary>?>(
                new PagedResult<RemoteCustomerSummary>(_customers, page, size, _customers.Count)
            );
        }

        public Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
            int page,
            int size,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }
}
