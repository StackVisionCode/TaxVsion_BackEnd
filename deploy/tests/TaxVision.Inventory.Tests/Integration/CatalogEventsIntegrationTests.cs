using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CatalogIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Inventory.Application.Abstractions;
using TaxVision.Inventory.Application.Consumers;
using TaxVision.Inventory.Infrastructure.Persistence;
using TaxVision.Inventory.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Inventory.Tests.Integration;

/// <summary>
/// INV-3 e INV-5 por el pipeline real de Wolverine, en una base temporal propia. Los consumers leen con candado, y el
/// candado exige la transacción que abre el middleware: fuera de ella cada evento de Catalog terminaría en la DLQ.
/// </summary>
public sealed class CatalogEventsIntegrationTests : IAsyncLifetime
{
    private const string EnvelopeSchema = "invcatalog";
    private const string Prefix = "invint_";
    private static readonly DateTime T0 = new(2026, 11, 6, 10, 0, 0, DateTimeKind.Utc);

    private readonly string _connectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("INVENTORY_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("e3b1a9f6-2c8d-4a17-9b54-7f2e6c1d8a90")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException(
                "Set INVENTORY_DB_CONNECTION or the Inventory user-secrets to run this test."
            )
    )
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private readonly Guid _tenantId = Guid.NewGuid();
    private IHost? _host;

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();
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
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact(Timeout = 120_000)]
    public async Task CreatedDeactivatedReactivated_EndsTracked()
    {
        var itemId = Guid.NewGuid();
        var bus = _host!.Services.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Created(itemId));
        await bus.InvokeAsync(Deactivated(itemId, T0.AddMinutes(1)));
        Assert.False(await IsTrackedAsync(itemId));

        await bus.InvokeAsync(Reactivated(itemId, T0.AddMinutes(2)));

        Assert.True(await IsTrackedAsync(itemId));
    }

    [Fact(Timeout = 120_000)]
    public async Task DeactivatedBeforeCreated_LeavesOneUntrackedLevel()
    {
        var itemId = Guid.NewGuid();
        var bus = _host!.Services.GetRequiredService<IMessageBus>();

        await bus.InvokeAsync(Deactivated(itemId, T0.AddMinutes(1)));
        await bus.InvokeAsync(Created(itemId));

        Assert.False(await IsTrackedAsync(itemId));
    }

    private CatalogItemCreatedIntegrationEvent Created(Guid itemId) =>
        new()
        {
            TenantId = _tenantId,
            OccurredOn = T0,
            ItemId = itemId,
            CategoryId = Guid.NewGuid(),
            Name = "Widget",
            Kind = "Product",
            TrackInventory = true,
            UnitPrice = 100,
            Currency = "USD",
        };

    private CatalogItemDeactivatedIntegrationEvent Deactivated(Guid itemId, DateTime occurredOn) =>
        new()
        {
            TenantId = _tenantId,
            OccurredOn = occurredOn,
            ItemId = itemId,
            TrackInventory = true,
        };

    private CatalogItemTrackingChangedIntegrationEvent Reactivated(Guid itemId, DateTime occurredOn) =>
        new()
        {
            TenantId = _tenantId,
            OccurredOn = occurredOn,
            ItemId = itemId,
            TrackInventory = true,
            IsActive = true,
        };

    private async Task<bool> IsTrackedAsync(Guid itemId)
    {
        await using var db = NewContext();
        return await db
            .StockLevels.IgnoreQueryFilters()
            .Where(s => s.TenantId == _tenantId && s.CatalogItemId == itemId)
            .Select(s => s.IsTracked)
            .SingleAsync();
    }

    private async Task<IHost> StartHostAsync() =>
        await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "inv-3-catalog-events-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CatalogItemCreatedConsumer))
                    .IncludeType(typeof(CatalogItemDeactivatedConsumer))
                    .IncludeType(typeof(CatalogItemTrackingChangedConsumer));
                options.PersistMessagesWithSqlServer(_connectionString, EnvelopeSchema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                // Como en DependencyInjection.cs.
                options.Services.AddDbContext<InventoryDbContext>(db => db.UseSqlServer(_connectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InventoryDbContext>());
                options.Services.AddScoped<IStockRepository, StockRepository>();
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, NoCorrelation>();
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, InventoryDbContext>();
                options.Policies.AutoApplyTransactions();
            })
            .StartAsync();

    private InventoryDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<InventoryDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenantContext()
        );

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{Prefix}%' AND create_date < DATEADD(HOUR, -1, GETDATE());
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;

        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string CorrelationId => string.Empty;

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Nothing();

        private sealed class Nothing : IDisposable
        {
            public void Dispose() { }
        }
    }
}
