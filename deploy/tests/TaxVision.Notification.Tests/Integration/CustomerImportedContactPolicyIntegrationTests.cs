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
/// Contra SQL Server, en base temporal: un cliente importado llega con el aviso de su bloque y su política real;
/// un evento más viejo que el aplicado no la pisa (el sello persiste en la columna).
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
    public async Task An_imported_client_gets_its_real_policy_and_a_redelivery_changes_nothing()
    {
        var imported = Guid.NewGuid();
        var evt = Chunk(DateTime.UtcNow, Card(imported, isNew: true, emailEnabled: false, smsEnabled: false));

        await SendAsync(evt);
        await SendAsync(evt);

        Assert.Equal((false, false), await ReadAsync(imported));
        Assert.Equal(1, await CountAsync());
    }

    [Fact]
    public async Task An_older_event_after_the_chunk_does_not_overwrite_it()
    {
        var customer = Guid.NewGuid();
        var at = DateTime.UtcNow;
        await SendAsync(Chunk(at, Card(customer, isNew: true, emailEnabled: false, smsEnabled: false)));

        await SendAsync(Updated(customer, at.AddMinutes(-1)));
        Assert.Equal((false, false), await ReadAsync(customer));

        await SendAsync(Updated(customer, at.AddMinutes(1)));
        Assert.Equal((true, true), await ReadAsync(customer));
    }

    private Task SendAsync(object evt) => _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(evt);

    private CustomersImportChunkCommittedIntegrationEvent Chunk(DateTime at, params ImportedCustomerCard[] cards) =>
        new()
        {
            TenantId = _tenant,
            CorrelationId = "import-chunk",
            OccurredOn = at,
            ImportJobId = Guid.NewGuid(),
            ChunkNumber = 1,
            CreatedByUserId = Guid.NewGuid(),
            Customers = cards,
        };

    private static ImportedCustomerCard Card(
        Guid customerId,
        bool isNew,
        bool emailEnabled = true,
        bool smsEnabled = true,
        string language = "En",
        string? email = "client@example.com",
        string name = "Client"
    ) =>
        new()
        {
            CustomerId = customerId,
            IsNew = isNew,
            Kind = "Individual",
            DisplayName = name,
            PrimaryEmail = email,
            Language = language,
            Status = "Active",
            EmailEnabled = emailEnabled,
            SmsEnabled = smsEnabled,
        };

    private CustomerUpdatedIntegrationEvent Updated(
        Guid customerId,
        DateTime at,
        bool emailEnabled = true,
        bool smsEnabled = true,
        string email = "old@example.com",
        string name = "Old"
    ) =>
        new()
        {
            TenantId = _tenant,
            OccurredOn = at,
            CustomerId = customerId,
            DisplayName = name,
            PrimaryEmail = email,
            Language = "En",
            EmailEnabled = emailEnabled,
            SmsEnabled = smsEnabled,
            ModifiedByUserId = Guid.NewGuid(),
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
