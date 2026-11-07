using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.Hosting;
using JasperFx;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Application.Tasks.DomainEvents;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.Series;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Piloto de UseTaxVisionDomainEvents() en Tasks contra SQL Server local. Host mínimo de Wolverine con el
/// TasksDbContext real y los sobres en un esquema propio (se borra al terminar). Los eventos de integración van
/// a un puerto TCP cerrado, así que se quedan en wolverine_outgoing_envelopes y se pueden contar.
/// </summary>
public sealed class TaskDomainEventDispatchIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        TasksIntegrationDatabase.ConnectionString;

    private readonly string _schema = $"tskbb1_{Guid.NewGuid():N}"[..15];
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly FailAfterSaveMetrics _metrics = new();
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
            await db.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == _tenantId).ExecuteDeleteAsync();
            await db.TaskSeries.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ExecuteDeleteAsync();
        }
        await DropSchemaAsync();
    }

    [Fact]
    public async Task CreateTask_EnvelopeStoredInSameTransaction()
    {
        var assignee = Guid.NewGuid();
        var created = await _host.Services.GetRequiredService<IMessageBus>().InvokeAsync<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>>(Command(assignee));

        Assert.True(created.IsSuccess);
        Assert.Equal(1, await CountTasksAsync());
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TaskCreatedDomainEvent"));

        // El handler del evento de dominio devuelve un aviso de cada uno, ni más ni menos.
        Assert.True(await WaitUntilAsync(async () => await CountRowsAsync("wolverine_outgoing_envelopes", "TaskAssignedIntegrationEvent") == 1));
        Assert.Equal(1, await CountRowsAsync("wolverine_outgoing_envelopes", "TaskCreatedIntegrationEvent"));
        Assert.Equal(1, await CountRowsAsync("wolverine_outgoing_envelopes", "ReminderRequestedIntegrationEvent"));
    }

    [Fact]
    public async Task FailureAfterSave_NoRowNoEnvelope()
    {
        _metrics.Fail = true;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _host.Services.GetRequiredService<IMessageBus>().InvokeAsync<BuildingBlocks.Results.Result<TaxVision.Tasks.Application.Tasks.TaskResponse>>(Command(Guid.NewGuid()))
        );

        Assert.Equal(0, await CountTasksAsync());
        Assert.Equal(0, await CountRowsAsync("wolverine_incoming_envelopes", "TaskCreatedDomainEvent"));
    }

    // Advertencia 2: fuera de un handler, el job guarda con TransactionalJobUnit (IDbContextOutbox) y el evento no se pierde.
    [Fact]
    public async Task SeriesJob_EnvelopeStoredThroughJobUnit()
    {
        await using (var seed = NewContext())
        {
            seed.TaskSeries.Add(ResumeTaskSeriesHandlerTests.NewSeries(_tenantId));
            await seed.SaveChangesAsync();
        }

        await SeriesMaterializationJobTests.InvokeRunOnceAsync(_host.Services);

        Assert.Equal(1, await CountTasksAsync());
        Assert.Equal(1, await CountRowsAsync("wolverine_incoming_envelopes", "TaskCreatedDomainEvent"));
        Assert.True(await WaitUntilAsync(async () => await CountRowsAsync("wolverine_outgoing_envelopes", "ReminderRequestedIntegrationEvent") == 1));
        Assert.Equal(1, await CountRowsAsync("wolverine_outgoing_envelopes", "TaskCreatedIntegrationEvent"));
        Assert.Equal(0, await CountRowsAsync("wolverine_outgoing_envelopes", "TaskAssignedIntegrationEvent"));
    }

    private CreateTaskCommand Command(Guid assignee) =>
        new(_tenantId, Guid.NewGuid(), "Prepare return", null, TaskPriority.Normal, assignee, null, null, DateTime.UtcNow.AddDays(3), "America/New_York", false, null);

    private async Task<IHost> StartHostAsync()
    {
        var closedPort = FreeTcpPort();
        var tenantId = _tenantId;
        return await Host.CreateDefaultBuilder()
            .UseWolverine(options =>
            {
                options.ServiceName = "tsk-bb1-domain-events-test";
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CreateTaskHandler))
                    .IncludeType(typeof(TaskCreatedDomainEventHandler));
                options.PersistMessagesWithSqlServer(ConnectionString, _schema);
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.KeepAfterMessageHandling = TimeSpan.FromMinutes(5);

                // Como en Program.cs: AddDbContext normal, sin AddDbContextWithWolverineIntegration.
                options.Services.AddDbContext<TasksDbContext>(db => db.UseSqlServer(ConnectionString));
                options.Services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TasksDbContext>());
                options.Services.AddSingleton<ITenantContext, NoTenantContext>();
                options.Services.AddSingleton<ICorrelationContext>(new NoOpCorrelationContext());
                options.Services.AddSingleton<ITaskMetrics>(_metrics);
                options.Services.AddScoped<ITaskRepository, TaskRepository>();
                options.Services.AddScoped<ITaskSeriesRepository>(sp => new SeriesMaterializationIntegrationTests.OwnTenantSeries(sp.GetRequiredService<TasksDbContext>(), tenantId));
                options.Services.AddScoped<ITaskSeriesMaterializer, TaskSeriesMaterializer>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TasksDbContext>();
                options.Policies.AutoApplyTransactions();

                options.UseTaxVisionDomainEvents();
                options.LocalQueue(DomainEventDispatchExtensions.DomainEventsQueue).Sequential();

                options.PublishMessage<TaskCreatedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<TaskAssignedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
                options.PublishMessage<ReminderRequestedIntegrationEvent>().ToPort(closedPort).UseDurableOutbox();
            })
            .StartAsync();
    }

    private Task<int> CountTasksAsync() =>
        CountAsync($"SELECT COUNT(*) FROM [dbo].[Tasks] WHERE TenantId = '{_tenantId}'");

    private Task<int> CountRowsAsync(string table, string messageType) =>
        CountAsync($"IF OBJECT_ID('[{_schema}].[{table}]') IS NULL SELECT 0 ELSE SELECT COUNT(*) FROM [{_schema}].[{table}] WHERE message_type LIKE '%{messageType}%'");

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

    // Borra las tablas de Wolverine del esquema de prueba y el esquema.
    private Task DropSchemaAsync() => DropSchemaAsync(_schema);

    private static Task DropSchemaAsync(string schema) =>
        ExecuteAsync(
            $"""
            DECLARE @sql nvarchar(max) = N'';
            SELECT @sql += N'ALTER TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N' DROP CONSTRAINT ' + QUOTENAME(f.name) + N';'
              FROM sys.foreign_keys f JOIN sys.tables t ON f.parent_object_id = t.object_id JOIN sys.schemas s ON t.schema_id = s.schema_id
             WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TABLE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(t.name) + N';'
              FROM sys.tables t JOIN sys.schemas s ON t.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP PROCEDURE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(p.name) + N';'
              FROM sys.procedures p JOIN sys.schemas s ON p.schema_id = s.schema_id WHERE s.name = N'{schema}';
            SELECT @sql += N'DROP TYPE ' + QUOTENAME(s.name) + N'.' + QUOTENAME(y.name) + N';'
              FROM sys.types y JOIN sys.schemas s ON y.schema_id = s.schema_id WHERE s.name = N'{schema}' AND y.is_user_defined = 1;
            IF SCHEMA_ID(N'{schema}') IS NOT NULL SET @sql += N'DROP SCHEMA ' + QUOTENAME(N'{schema}') + N';';
            EXEC sp_executesql @sql;
            """
        );

    private static TasksDbContext NewContext() =>
        new(new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options, new NoTenantContext());

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

    // Falla después del SaveChanges del handler: la transacción de Wolverine tiene que deshacer fila y sobre.
    private sealed class FailAfterSaveMetrics : ITaskMetrics
    {
        public bool Fail { get; set; }

        public void RecordCreated(bool hasCustomer)
        {
            if (Fail)
                throw new InvalidOperationException("Fallo forzado después de guardar.");
        }

        public void RecordCompleted(bool hasCustomer) { }

        public void RecordBlocked() { }

        public void RecordDependencyCycleRejected() { }

        public void RecordReconciliationCorrections(int count) { }

        public void RecordTimeToCompleteSeconds(double seconds) { }

        public void RecordOverdue(int count) { }
    }
}
