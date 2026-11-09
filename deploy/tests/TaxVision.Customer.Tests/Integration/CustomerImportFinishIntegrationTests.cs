using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Imports.Messages;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Imports;
using TaxVision.Customer.Infrastructure.Persistence;
using TaxVision.Customer.Infrastructure.Persistence.Repositories;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Customer.Tests.Integration;

/// <summary>
/// CUS-BB3 contra SQL Server local: cerrar la importación (Completed o Failed) y su aviso van en la misma
/// transacción. Host mínimo de Wolverine con el CustomerDbContext real; los sobres en un esquema propio que se
/// borra al terminar. Los avisos van a un puerto TCP cerrado, así que se quedan en wolverine_outgoing_envelopes.
/// </summary>
public sealed class CustomerImportFinishIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString = CustomerIntegrationDatabase.ConnectionString;

    private readonly string _schema = $"cusbb3_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly FailAfterSaveInterceptor _interceptor = new();
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
            await db
                .CustomerImportAttempts.IgnoreQueryFilters()
                .Where(a => a.TenantId == _tenantId)
                .ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task FailureAfterSave_LeavesAttemptNotCompletedAndNoEnvelope()
    {
        var attemptId = await SeedApplyingAttemptAsync();
        _interceptor.FailWhenSaving = ImportStatus.Completed;

        await Assert.ThrowsAnyAsync<Exception>(() => FinishAsync(attemptId));

        Assert.NotEqual(ImportStatus.Completed, await StatusOfAsync(attemptId));
        Assert.Equal(0, await CountEnvelopesAsync(nameof(CustomersBulkImportedIntegrationEvent)));
    }

    [Fact]
    public async Task Finish_StoresTerminalStateAndNoticeTogether()
    {
        var attemptId = await SeedApplyingAttemptAsync();

        await FinishAsync(attemptId);

        Assert.Equal(ImportStatus.Completed, await StatusOfAsync(attemptId));
        Assert.Equal(1, await CountEnvelopesAsync(nameof(CustomersBulkImportedIntegrationEvent)));
    }

    [Fact]
    public async Task FailAttempt_FailureAfterSave_LeavesAttemptNotFailedAndNoEnvelope()
    {
        var attemptId = await SeedApplyingAttemptAsync();
        _interceptor.FailWhenSaving = ImportStatus.Failed;

        await Assert.ThrowsAnyAsync<Exception>(() =>
            ImportAttemptLifecycle.FailAsync(Scopes, attemptId, "File is empty.", CancellationToken.None)
        );

        Assert.Equal(ImportStatus.Applying, await StatusOfAsync(attemptId));
        Assert.Equal(0, await CountEnvelopesAsync(nameof(CustomerImportFailedIntegrationEvent)));
    }

    private IServiceScopeFactory Scopes => _host.Services.GetRequiredService<IServiceScopeFactory>();

    private Task FinishAsync(Guid attemptId) =>
        ImportAttemptLifecycle.FinishAsync(
            Scopes,
            attemptId,
            new ImportStartInfo(_tenantId, _userId, DuplicateStrategy.Skip, ImportSourceKind.Csv),
            new ImportChunkAggregate(3, 0, 0, 0, [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()], []),
            total: 3,
            NullLogger.Instance,
            CancellationToken.None
        );

    private async Task<Guid> SeedApplyingAttemptAsync()
    {
        var attempt = CustomerImportAttempt
            .Create(
                _tenantId,
                _userId,
                $"cusbb3-{Guid.NewGuid():N}",
                DuplicateStrategy.Skip,
                ImportSourceKind.Csv,
                "three.csv"
            )
            .Value;
        attempt.Start();
        attempt.SetTotalRows(3);
        attempt.MoveToApplying();
        await using var db = NewContext();
        db.CustomerImportAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return attempt.Id;
    }

    private async Task<ImportStatus> StatusOfAsync(Guid attemptId)
    {
        await using var db = NewContext();
        return await db
            .CustomerImportAttempts.IgnoreQueryFilters()
            .Where(a => a.Id == attemptId)
            .Select(a => a.Status)
            .SingleAsync();
    }

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "cus-bb3-import-finish-test";
                options.Discovery.DisableConventionalDiscovery();
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<CustomerDbContext>(db =>
                    db.UseSqlServer(ConnectionString).AddInterceptors(_interceptor)
                );
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<CustomerDbContext>());
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext, NoCorrelation>();
                options.Services.AddScoped<ICustomerImportRepository, CustomerImportRepository>();
                options.Services.AddSingleton<ICustomerImportCloudStorageClient, NoCloudStorage>();
                options.Services.AddSingleton<ITransactionalUnitRunner, CustomerTransactionalUnitRunner>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, CustomerDbContext>();
                options.Policies.AutoApplyTransactions();

                options.PublishMessage<CustomersBulkImportedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<CustomerImportFailedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<int> CountEnvelopesAsync(string messageType) =>
        CountAsync(
            $"IF OBJECT_ID('[{_schema}].[wolverine_outgoing_envelopes]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[wolverine_outgoing_envelopes] WHERE message_type LIKE '%{messageType}%'"
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
        new(
            new DbContextOptionsBuilder<CustomerDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

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
        public string CorrelationId => "cus-bb3-test";

        public void Set(string correlationId) { }

        public IDisposable Push(string correlationId) => new Scope();

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }

    private sealed class NoCloudStorage : ICustomerImportCloudStorageClient
    {
        public Task<Result> UploadAsync(
            Guid t,
            Guid f,
            byte[] c,
            string n,
            string ct,
            Guid a,
            Wolverine.IMessageBus bus,
            CancellationToken token = default
        ) => Task.FromResult(Result.Success());

        public Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success(Array.Empty<byte>()));

        public Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    // Falla después de guardar el intento en el estado indicado y antes de confirmar, como un corte a mitad.
    private sealed class FailAfterSaveInterceptor : SaveChangesInterceptor
    {
        public ImportStatus? FailWhenSaving { get; set; }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            var hit =
                FailWhenSaving is { } status
                && eventData
                    .Context!.ChangeTracker.Entries<CustomerImportAttempt>()
                    .Any(e => e.Entity.Status == status);
            return hit
                ? ValueTask.FromException<int>(new InvalidOperationException("Fallo forzado después de guardar."))
                : ValueTask.FromResult(result);
        }
    }
}
