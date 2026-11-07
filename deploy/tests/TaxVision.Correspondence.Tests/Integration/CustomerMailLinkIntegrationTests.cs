using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Backfill;
using TaxVision.Correspondence.Application.Ingest;
using TaxVision.Correspondence.Application.Projections.CustomerEvents;
using TaxVision.Correspondence.Domain.Inbox;
using TaxVision.Correspondence.Domain.ValueObjects;
using TaxVision.Correspondence.Infrastructure.Persistence;
using TaxVision.Correspondence.Infrastructure.Persistence.Repositories;
using TaxVision.Correspondence.Tests.Projections;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;

namespace TaxVision.Correspondence.Tests.Integration;

/// <summary>
/// COR-05 y COR-08 por el pipeline real, en base temporal propia con colas locales durables: alta de cliente, backfill
/// por páginas y enlace del correo.
/// </summary>
public sealed class CustomerMailLinkIntegrationTests : IAsyncLifetime
{
    private const string Address = "nuevo@cor05.test";
    private const string Prefix = "corn05_";

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly PagedCustomerClient _customers = new();
    private readonly string _connectionString = new SqlConnectionStringBuilder(CorrespondenceSqlServer.ConnectionString)
    {
        InitialCatalog = $"{Prefix}{Guid.NewGuid():N}"[..(Prefix.Length + 12)],
    }.ConnectionString;
    private IHost _host = null!;

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    private CorrespondenceDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<CorrespondenceDbContext>().UseSqlServer(_connectionString).Options,
            new FixedTenant(_tenantId)
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
                options.Discovery.IncludeType(typeof(CustomerCreatedConsumer));
                options.Discovery.IncludeType(typeof(LinkUnlinkedMailHandler));
                options.Discovery.IncludeType(typeof(BackfillTenantCustomersHandler));
                options.Durability.Mode = DurabilityMode.Solo;
                options.PersistMessagesWithSqlServer(_connectionString);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options
                    .UseEntityFrameworkCoreTransactions()
                    .WithDbContextAbstraction<IUnitOfWork, CorrespondenceDbContext>();
                options.Policies.AutoApplyTransactions();
                options.UseTaxVisionDurableLocalQueues();
                var services = options.Services;
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddDbContext<CorrespondenceDbContext>(db => db.UseSqlServer(_connectionString));
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CorrespondenceDbContext>());
                services.AddScoped<ICustomerEmailAddressRepository, CustomerEmailAddressRepository>();
                services.AddScoped<ITenantBackfillStateRepository, TenantBackfillStateRepository>();
                services.AddScoped<ITenantCustomerBackfillService, TenantCustomerBackfillService>();
                services.AddScoped<IIncomingEmailRepository, IncomingEmailRepository>();
                services.AddScoped<IEmailThreadRepository, EmailThreadRepository>();
                services.AddSingleton<ICorrespondenceCustomerClient>(_customers);
                services.AddScoped<ICorrelationContext, FakeCorrelationContext>();
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

    private Task InvokeAsync(object message) => _host.Services.GetRequiredService<IMessageBus>().InvokeAsync(message);

    private async Task<(Guid ThreadId, Guid EmailId)> SeedNotAClientMailAsync()
    {
        var at = DateTime.UtcNow;
        var thread = EmailThread.NewFromMessage(_tenantId, null, "Subject", "pt-cor05", at).Value;
        var email = IncomingEmail
            .Create(
                _tenantId,
                null,
                thread.Id,
                Guid.NewGuid(),
                "gmail",
                "pm-cor05",
                EmailAddress.Create(Address).Value,
                null,
                "Subject",
                "",
                at,
                false,
                0
            )
            .Value;
        await using var seed = NewContext();
        seed.EmailThreads.Add(thread);
        seed.IncomingEmails.Add(email);
        await seed.SaveChangesAsync();
        return (thread.Id, email.Id);
    }

    private async Task<Guid?> WaitForEmailCustomerAsync(Guid emailId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (true)
        {
            await using var db = NewContext();
            var email = await db.IncomingEmails.AsNoTracking().FirstAsync(x => x.Id == emailId);
            if (email.CustomerId is not null || DateTime.UtcNow > deadline)
                return email.CustomerId;
            await Task.Delay(100);
        }
    }

    [Fact]
    public async Task CustomerCreatedAfterWriting_MailMovesToTheirFile()
    {
        var (threadId, emailId) = await SeedNotAClientMailAsync();
        var customerId = Guid.NewGuid();

        await InvokeAsync(
            new CustomerCreatedIntegrationEvent
            {
                TenantId = _tenantId,
                CorrelationId = "cor-05",
                CustomerId = customerId,
                Kind = "Individual",
                DisplayName = "Nuevo",
                PrimaryEmail = Address,
                Language = "en",
                CreatedByUserId = Guid.NewGuid(),
            }
        );

        Assert.Equal(customerId, await WaitForEmailCustomerAsync(emailId));
        await using var check = NewContext();
        Assert.Equal(
            customerId,
            (await check.EmailThreads.AsNoTracking().FirstAsync(x => x.Id == threadId)).CustomerId
        );
    }

    [Fact]
    public async Task Backfill_SavesOnePagePerRunAndFinishes()
    {
        var (_, emailId) = await SeedNotAClientMailAsync();
        var customerId = Guid.NewGuid();
        _customers.Pages[1] = ([new RemoteCustomerSummary(Guid.NewGuid(), "first@cor08.test", true)], true);
        _customers.Pages[2] = ([new RemoteCustomerSummary(customerId, Address, true)], false);

        await InvokeAsync(new BackfillTenantCustomersCommand(_tenantId));

        // La segunda página llega por la cola local, y con ella el enlace del correo.
        Assert.Equal(customerId, await WaitForEmailCustomerAsync(emailId));
        await using var check = NewContext();
        var state = await check.TenantBackfillStates.AsNoTracking().FirstAsync(x => x.TenantId == _tenantId);
        Assert.True(state.IsCompleted);
        Assert.Equal(2, state.LastConfirmedPage);
        Assert.Equal(2, await check.CustomerEmailAddresses.CountAsync(x => x.TenantId == _tenantId));
    }
}

public sealed class PagedCustomerClient : ICorrespondenceCustomerClient
{
    public Dictionary<int, (IReadOnlyList<RemoteCustomerSummary> Items, bool HasMore)> Pages { get; } = [];

    public Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var (items, hasMore) = Pages.GetValueOrDefault(page, ([], false));
        var total = hasMore ? page * size + 1 : (page - 1) * size + items.Count;
        return Task.FromResult<PagedResult<RemoteCustomerSummary>?>(
            new PagedResult<RemoteCustomerSummary>(items, page, size, total)
        );
    }
}
