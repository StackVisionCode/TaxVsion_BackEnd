using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.Archive;
using TaxVision.Customer.Application.Customers.DomainEvents;
using TaxVision.Customer.Domain.Customers;
using TaxVision.Customer.Domain.Customers.ValueObjects;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// CUS-BB1 contra SQL Server local: UseTaxVisionDomainEvents() con el CustomerDbContext real. Host mínimo de
/// Wolverine, sobres en un esquema propio que se borra al terminar y avisos a un puerto TCP cerrado (se quedan en
/// wolverine_outgoing_envelopes y se pueden contar).
/// </summary>
public sealed class CustomerDomainEventIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        Environment.GetEnvironmentVariable("CUSTOMER_DB_CONNECTION")
        ?? new ConfigurationBuilder()
            .AddUserSecrets("8c4e7963-1ddb-441e-af2d-755d58fb86a3")
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Set CUSTOMER_DB_CONNECTION or the Customer user-secrets.");

    private readonly string _schema = $"cusbb1_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FailAfterArchiveInterceptor _interceptor = new();
    private IHost _host = null!;

    public async Task InitializeAsync()
    {
        await ExecuteAsync($"EXEC('CREATE SCHEMA [{_schema}]')");
        _host = await StartHostAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        await using (var db = NewContext())
        {
            await db.Customers.IgnoreQueryFilters().Where(c => c.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task ArchiveCustomer_EnvelopeStoredWithRow()
    {
        var customerId = await SeedCustomerAsync();

        var result = await ArchiveAsync(customerId);

        Assert.True(result.IsSuccess);
        Assert.Equal(CustomerStatus.Archived, await StatusOfAsync(customerId));
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "CustomerArchivedDomainEvent"));

        // El handler del evento de dominio devuelve un solo aviso de integración.
        Assert.True(
            await WaitUntilAsync(async () =>
                await CountRowsAsync("wolverine_outgoing_envelopes", nameof(CustomerArchivedIntegrationEvent)) == 1
            )
        );
    }

    [Fact]
    public async Task ArchiveCustomer_FailureAfterSave_NoChangeNoEnvelope()
    {
        var customerId = await SeedCustomerAsync();
        _interceptor.Fail = true;

        await Assert.ThrowsAnyAsync<Exception>(() => ArchiveAsync(customerId));

        Assert.Equal(CustomerStatus.Active, await StatusOfAsync(customerId));
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "CustomerArchivedDomainEvent"));
        Assert.Equal(0, await CountRowsAsync("wolverine_outgoing_envelopes", nameof(CustomerArchivedIntegrationEvent)));
    }

    private Task<Result> ArchiveAsync(Guid customerId) =>
        _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result>(new ArchiveCustomerCommand(_tenantId, customerId, _userId));

    private async Task<Guid> SeedCustomerAsync()
    {
        var customer = DomainCustomer
            .Register(
                _tenantId,
                CustomerKind.Individual,
                PersonalName.Create("Bb1", "Archive").Value,
                null,
                EmailAddress.Create($"cus-bb1-{Guid.NewGuid():N}@example.test").Value,
                null,
                Language.En,
                _userId
            )
            .Value;
        await using var db = NewContext();
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return customer.Id;
    }

    private async Task<CustomerStatus> StatusOfAsync(Guid customerId)
    {
        await using var db = NewContext();
        return await db.Customers.IgnoreQueryFilters().Where(c => c.Id == customerId).Select(c => c.Status).SingleAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "cus-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(ArchiveCustomerHandler))
                    .IncludeType(typeof(CustomerArchivedDomainEventHandler));
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<CustomerDbContext>(db =>
                    db.UseSqlServer(ConnectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomerDbContext>());
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, NoCorrelation>();
                options.Services.AddScoped<ICustomerRepository, CustomerRepository>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CustomerDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();

                options.PublishMessage<CustomerArchivedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<FilesDeletionRequestedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'"
        );

    private static async Task<int> CountAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    // Borra las tablas de Wolverine del esquema de prueba (primero las FK) y el esquema.
    private Task DropSchemaAsync() =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{_schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{_schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{_schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{_schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );

    private static CustomerDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(ConnectionString).Options, new NoTenantContext());

    private static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
                return true;
            await Task.Delay(200);
        }
        return false;
    }

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }

    private sealed class NoCorrelation : ICorrelationContext
    {
        public string CorrelationId => "cus-bb1-test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    // Falla después de guardar el archivado y antes de confirmar: la transacción de Wolverine deshace fila y sobre.
    private sealed class FailAfterArchiveInterceptor : SaveChangesInterceptor
    {
        public bool Fail { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                Fail
                && eventData.Context!.ChangeTracker.Entries<DomainCustomer>().Any(e => e.Entity.Status == CustomerStatus.Archived);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
