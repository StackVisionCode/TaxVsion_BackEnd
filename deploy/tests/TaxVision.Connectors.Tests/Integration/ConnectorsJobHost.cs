using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Infrastructure.Resilience;
using BuildingBlocks.Messaging.ConnectorsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Common;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.Application.Accounts.DomainEvents;
using TaxVision.Connectors.Application.OAuth;
using TaxVision.Connectors.Application.Watch;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Infrastructure.Accounts;
using TaxVision.Connectors.Infrastructure.OAuth;
using TaxVision.Connectors.Infrastructure.Persistence;
using TaxVision.Connectors.Infrastructure.Persistence.Repositories;
using TaxVision.Connectors.Infrastructure.Watch;
using TaxVision.Connectors.Tests.OAuth;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Connectors.Tests.Integration;

/// <summary>
/// Host mínimo de Wolverine con la persistencia de Connectors sobre una base temporal propia: los jobs recorren
/// todos los buzones, así que nunca corren contra la base de desarrollo. Los avisos van a un puerto TCP cerrado y
/// se quedan en wolverine_outgoing_envelopes. Si una corrida anterior se abortó, su base se borra al arrancar.
/// </summary>
internal sealed class ConnectorsJobHost : IAsyncDisposable
{
    private readonly string _prefix;
    private IHost? _host;

    public ConnectorsJobHost(string prefix)
    {
        _prefix = prefix;
        ConnectionString = new SqlConnectionStringBuilder(BaseConnection)
        {
            InitialCatalog = $"{prefix}_{Guid.NewGuid():N}"[..(prefix.Length + 9)],
        }.ConnectionString;
    }

    public string ConnectionString { get; }
    public FailAfterSaveInterceptor FailAfterSave { get; } = new();
    public OpenTransactionsCounter Transactions { get; } = new();
    public IServiceProvider Services => _host!.Services;

    private static string BaseConnection =>
        Environment.GetEnvironmentVariable("CONNECTORS_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("d5a2f07b-0550-4e76-a692-df921f63f798")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set CONNECTORS_DB_CONNECTION or the Connectors Api user-secrets.");

    public async Task StartAsync(Action<IServiceCollection> configureServices)
    {
        await DropOrphansAsync();
        await using (var db = NewContext())
            await db.Database.EnsureCreatedAsync();

        var closedPort = FreeTcpPort();
        try
        {
            _host = await Host.CreateDefaultBuilder()
                .UseWolverine(options =>
                {
                    options.ServiceName = "connectors-int-test";
                    options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                    options.Discovery.DisableConventionalDiscovery();
                    options.PersistMessagesWithSqlServer(ConnectionString, "wolverine");
                    options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                    options.Durability.Mode = DurabilityMode.Solo;
                    options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);
                    options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                    options.Services.AddBuildingBlocks();
                    options.Services.AddDbContext<ConnectorsDbContext>(db =>
                        db.UseSqlServer(ConnectionString).AddInterceptors(FailAfterSave, Transactions)
                    );
                    options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<ConnectorsDbContext>());
                    options.Services.AddScoped<ITenantEmailAccountRepository, TenantEmailAccountRepository>();
                    options.Services.AddScoped<IOAuthConnectionRepository, OAuthConnectionRepository>();
                    options.Services.AddScoped<
                        IProviderWatchSubscriptionRepository,
                        ProviderWatchSubscriptionRepository
                    >();
                    options.Services.AddScoped<IWatchRenewalService, WatchRenewalService>();
                    options.Services.AddScoped<IOAuthTokenManager, OAuthTokenManager>();
                    options.Services.AddScoped<IMailboxFailureUnit, MailboxFailureUnit>();
                    options.Services.AddSingleton(new HttpResiliencePipelineRegistry());
                    options.Services.AddSingleton<IEncryptedSecretProtector, FakeEncryptedSecretProtector>();
                    options.Services.AddSingleton<IDistributedLock, InMemoryDistributedLock>();
                    configureServices(options.Services);

                    options
                        .UseEntityFrameworkCoreTransactions()
                        .WithDbContextAbstraction<IUnitOfWork, ConnectorsDbContext>();
                    options.Policies.AutoApplyTransactions();
                    options.Discovery.IncludeType(typeof(TenantEmailAccountErroredDomainEventHandler));
                    options.Discovery.IncludeType(typeof(GraphLifecycleNotificationHandler));
                    options.UseTaxVisionDomainEvents();
                    options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();
                    options.Policies.AddMiddleware(typeof(BuildingBlocks.Web.Tenancy.LocalCommandTenantMiddleware));

                    options
                        .PublishMessage<ConnectorsOAuthRefreshFailedIntegrationEvent>()
                        .ToPort(closedPort)
                        .UseDurableOutbox();
                    options
                        .PublishMessage<ConnectorsWatchExpiredIntegrationEvent>()
                        .ToPort(closedPort)
                        .UseDurableOutbox();
                })
                .StartAsync();
        }
        catch
        {
            // Si el host no arranca, xUnit no llama DisposeAsync: la base temporal se borra aquí.
            await DisposeAsync();
            throw;
        }
    }

    public ConnectorsDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ConnectorsDbContext>().UseSqlServer(ConnectionString).Options, new NoTenant());

    public async Task<int> CountEnvelopesAsync(string table, string messageType)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"IF OBJECT_ID('[wolverine].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [wolverine].[{table}] WHERE message_type LIKE '%{messageType}%'";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    public async Task<int> WaitForOutgoingAsync(string messageType)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var count = await CountEnvelopesAsync("wolverine_outgoing_envelopes", messageType);
            if (count > 0)
                return count;
            await Task.Delay(250);
        }
        return 0;
    }

    public async ValueTask DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
            _host = null;
        }
        SqlConnection.ClearAllPools();
        await using var db = NewContext();
        await db.Database.EnsureDeletedAsync();
    }

    // Una corrida abortada no llama DisposeAsync. Cada clase de prueba usa su prefijo, así que no choca con otra.
    private async Task DropOrphansAsync()
    {
        var master = new SqlConnectionStringBuilder(ConnectionString) { InitialCatalog = "master" }.ConnectionString;
        await using var connection = new SqlConnection(master);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER DATABASE ' + QUOTENAME(name) + N' SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE ' + QUOTENAME(name) + N';'
              FROM sys.databases WHERE name LIKE N'{_prefix}[_]%';
            EXEC sp_executesql @sql;
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NoTenant : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Cuántas transacciones explícitas hay abiertas ahora.</summary>
    internal sealed class OpenTransactionsCounter : DbTransactionInterceptor
    {
        private int _open;

        public int Open => Volatile.Read(ref _open);

        public override ValueTask<System.Data.Common.DbTransaction> TransactionStartedAsync(
            System.Data.Common.DbConnection connection,
            TransactionEndEventData eventData,
            System.Data.Common.DbTransaction result,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Increment(ref _open);
            return ValueTask.FromResult(result);
        }

        public override Task TransactionCommittedAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Decrement(ref _open);
            return Task.CompletedTask;
        }

        public override Task TransactionRolledBackAsync(
            System.Data.Common.DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            Interlocked.Decrement(ref _open);
            return Task.CompletedTask;
        }
    }

    /// <summary>Hace fallar el siguiente guardado después de ejecutarse el SQL.</summary>
    internal sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public bool Armed { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            if (!Armed)
                return ValueTask.FromResult(result);
            Armed = false;
            throw new InvalidOperationException("Fallo forzado después de guardar.");
        }
    }
}
