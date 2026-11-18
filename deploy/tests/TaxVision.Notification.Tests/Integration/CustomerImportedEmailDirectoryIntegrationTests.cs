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

/// <summary>
/// Contra SQL Server, en base temporal: los importados llegan al directorio con el aviso de su bloque, sin pedir los
/// correos a Customer. Un email vacío no borra el conocido y un evento más viejo no pisa.
/// </summary>
public sealed class CustomerImportedEmailDirectoryIntegrationTests : IAsyncLifetime
{
    private readonly NotificationTempDatabase _database = new("notn21_");
    private readonly Guid _tenant = Guid.NewGuid();
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
    public async Task Imported_clients_get_their_directory_row_from_the_chunk()
    {
        var created = Guid.NewGuid();
        var withoutEmail = Guid.NewGuid();

        await SendAsync(
            Chunk(
                DateTime.UtcNow,
                Card(created, isNew: true, email: "Created@Import.Test"),
                Card(withoutEmail, isNew: true, email: null)
            )
        );

        Assert.Equal("created@import.test", await EmailOfAsync(created));
        Assert.Null(await EmailOfAsync(withoutEmail));
    }

    [Fact]
    public async Task An_update_without_email_keeps_it_and_an_older_event_does_not_overwrite()
    {
        var customer = Guid.NewGuid();
        var at = DateTime.UtcNow;
        await SendAsync(Chunk(at, Card(customer, isNew: true, email: "client@import.test")));
        await SendAsync(Chunk(at.AddMinutes(1), Card(customer, isNew: false, email: null, name: "Renamed")));

        await SendAsync(Updated(customer, at.AddMinutes(-1), email: "stale@import.test"));

        Assert.Equal("client@import.test", await EmailOfAsync(customer));
    }

    private Task SendAsync(object evt) => _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(evt);
}
