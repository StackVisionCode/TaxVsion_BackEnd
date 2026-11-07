using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Notification.Application.Directory.Abstractions;
using TaxVision.Notification.Application.Directory.Consumers;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Infrastructure.Persistence;
using TaxVision.Notification.Infrastructure.Persistence.Repositories;
using Wolverine;

namespace TaxVision.Notification.Tests.Integration;

/// <summary>
/// CUS-9 contra SQL Server, en base temporal: un cliente importado (solo llega <c>CustomersBulkImported</c>,
/// sin <c>CustomerCreated</c>) tiene su fila de política, sin pisar lo que un evento ya proyectó.
/// </summary>
public sealed class CustomerImportedContactPolicyIntegrationTests : IAsyncLifetime
{
    private const string Prefix = "notcus9_";
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly string _connectionString = new SqlConnectionStringBuilder(
        Environment.GetEnvironmentVariable("NOTIFICATION_DB_CONNECTION")
            ?? new ConfigurationBuilder()
                .AddUserSecrets("f7c2d8e1-3b5a-4c97-8d1e-notification1")
                .Build()
                .GetConnectionString("Default")
            ?? throw new InvalidOperationException("Set NOTIFICATION_DB_CONNECTION or the Notification user-secrets.")
    )
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;

    private IHost _host = null!;

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    private NotificationDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<NotificationDbContext>().UseSqlServer(_connectionString).Options,
            new NoTenant()
        );

    public async Task InitializeAsync()
    {
        await DropLeftoversAsync();
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();

        _host = await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options.Discovery.DisableConventionalDiscovery();
                options.Discovery.IncludeType(typeof(CustomerContactPolicyConsumer));
                options.Durability.Mode = DurabilityMode.MediatorOnly;
                options.Services.AddSingleton<ITenantContext, NoTenant>();
                options.Services.AddDbContext<NotificationDbContext>(db => db.UseSqlServer(_connectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<NotificationDbContext>());
                options.Services.AddScoped<ICustomerContactPolicyRepository, CustomerContactPolicyRepository>();
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
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    [Fact]
    public async Task Imported_customers_get_their_row_and_a_redelivery_changes_nothing()
    {
        var imported = Guid.NewGuid();
        var updated = Guid.NewGuid();

        await SendAsync(Imported([imported], [updated]));
        await SendAsync(Imported([imported], [updated]));

        Assert.Equal((true, true), await ReadAsync(imported));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task A_row_already_projected_by_an_event_is_not_overwritten()
    {
        var customer = Guid.NewGuid();
        await using (var seed = NewContext())
        {
            seed.CustomerContactPolicyProjections.Add(
                CustomerContactPolicyProjection.Create(_tenant, customer, emailEnabled: false, smsEnabled: false, "Es")
            );
            await seed.SaveChangesAsync();
        }

        await SendAsync(Imported([customer], []));

        Assert.Equal((false, false), await ReadAsync(customer));
    }

    private Task SendAsync(CustomersBulkImportedIntegrationEvent evt) =>
        _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(evt);

    private CustomersBulkImportedIntegrationEvent Imported(Guid[] created, Guid[] updated) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "cus-9",
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

    private async Task<(bool Email, bool Sms)> ReadAsync(Guid customer)
    {
        await using var db = NewContext();
        var row = await db
            .CustomerContactPolicyProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(p => p.TenantId == _tenant && p.CustomerId == customer);
        return (row.EmailEnabled, row.SmsEnabled);
    }

    private async Task<int> CountAsync()
    {
        await using var db = NewContext();
        return await db.CustomerContactPolicyProjections.IgnoreQueryFilters().CountAsync(p => p.TenantId == _tenant);
    }

    // Solo las de más de una hora: las recientes pueden ser de otra prueba corriendo ahora.
    private async Task DropLeftoversAsync()
    {
        var master = new SqlConnectionStringBuilder(_connectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();

        var leftovers = new List<string>();
        await using (var find = connection.CreateCommand())
        {
            find.CommandText =
                $"SELECT name FROM sys.databases WHERE name LIKE '{Prefix.Replace("_", "[_]")}%' "
                + "AND create_date < DATEADD(HOUR, -1, GETDATE())";
            await using var reader = await find.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                leftovers.Add(reader.GetString(0));
        }

        foreach (var name in leftovers)
        {
            await using var drop = connection.CreateCommand();
            drop.CommandText =
                $"ALTER DATABASE [{name}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{name}];";
            await drop.ExecuteNonQueryAsync();
        }
    }
}
