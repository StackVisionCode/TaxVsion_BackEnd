using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using JasperFx;
using JasperFx.CodeGeneration.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TaxVision.Tasks.Application.Dependencies;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Hierarchy;
using TaxVision.Tasks.Application.Hierarchy.Abstractions;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Application.Tasks.Commands;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.ClientForms.Integration;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.SqlServer;
using Wolverine.Transports.Tcp;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// BB-14. Cerrar una tarea por InvokeAsync con un guardado que falla una vez como un timeout de SQL: el aviso de la
/// sucesora desbloqueada se cuenta al llegar a un puerto, con el servicio real de la cascada y una base temporal propia.
/// </summary>
public sealed class CompleteTaskRetryIntegrationTests : IAsyncLifetime
{
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(3);

    private readonly TasksTemporaryDatabase _database = new();
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly FailOnceInterceptor _failure = new();
    private IHost? _host;

    public Task InitializeAsync() => _database.CreateAsync();

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
    public async Task Complete_TransientSaveFailure_AnnouncesTheSuccessorOnce()
    {
        _host = await StartHostAsync();
        var (predecessor, successor) = await SeedBlockedSuccessorAsync();
        _failure.Armed = true;

        var result = await _host
            .Services.GetRequiredService<IMessageBus>()
            .InvokeAsync<Result<TaskResponse>>(new CompleteTaskCommand(_tenantId, predecessor, _owner, false));

        await Task.Delay(SettleTime);
        Assert.True(result.IsSuccess);
        Assert.Equal(1, _failure.Failures);
        Assert.Equal(1, TaskUnblockedProbeHandler.CountFor(successor));
    }

    private async Task<(Guid Predecessor, Guid Successor)> SeedBlockedSuccessorAsync()
    {
        var predecessor = NewTask();
        var successor = NewTask();
        successor.RegisterBlockerAdded();
        await using var db = _database.NewContext(_tenantId);
        db.Tasks.AddRange(predecessor, successor);
        db.TaskDependencies.Add(
            TaskDependency.Create(_tenantId, successor.Id, predecessor.Id, _owner, DateTime.UtcNow).Value
        );
        await db.SaveChangesAsync();
        return (predecessor.Id, successor.Id);
    }

    private TaskItem NewTask() =>
        TaskItem
            .Create(
                _tenantId,
                _owner,
                TaskTitle.Create("Prepare return").Value,
                null,
                TaskPriority.Normal,
                TaskReference.None,
                null,
                null,
                _owner,
                DateTime.UtcNow
            )
            .Value;

    private Task<IHost> StartHostAsync() =>
        Host.CreateDefaultBuilder()
            .ConfigureLogging(logging => logging.ClearProviders())
            .UseWolverine(options =>
            {
                var connectionString = _database.ConnectionString;
                var port = FreeTcpPort();
                options.ServiceName = "tasks-bb14-test";
                options.ServiceLocationPolicy = ServiceLocationPolicy.AllowedButWarn;
                options
                    .Discovery.DisableConventionalDiscovery()
                    .IncludeType(typeof(CompleteTaskHandler))
                    .IncludeType(typeof(TaskUnblockedProbeHandler));
                options.PersistMessagesWithSqlServer(connectionString, "wolverine_test");
                options.AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate;
                options.Durability.Mode = DurabilityMode.Solo;
                options.Durability.DrainTimeout = TimeSpan.FromSeconds(2);

                var services = options.Services;
                services.AddDbContext<TasksDbContext>(db =>
                    db.UseSqlServer(connectionString).AddInterceptors(_failure)
                );
                services.AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TasksDbContext>());
                services.AddSingleton<ITenantContext>(new FixedTenant(_tenantId));
                services.AddSingleton<ICorrelationContext>(new NoOpCorrelationContext());
                services.AddSingleton<ITaskMetrics>(new RecordingTaskMetrics());
                services.AddScoped<ITaskRepository, TaskRepository>();
                services.AddScoped<ITaskDependencyRepository, TaskDependencyRepository>();
                services.AddScoped<ITaskSeriesRepository, TaskSeriesRepository>();
                services.AddScoped<ITaskSeriesMaterializer, TaskSeriesMaterializer>();
                services.AddScoped<ITaskHierarchyService, TaskHierarchyService>();
                services.AddScoped<ITaskUnblockingService, TaskUnblockingService>();
                options.UseEntityFrameworkCoreTransactions().WithDbContextAbstraction<IUnitOfWork, TasksDbContext>();
                options.Policies.AutoApplyTransactions();

                options.ListenAtPort(port).UseDurableInbox();
                options.PublishMessage<TaskUnblockedIntegrationEvent>().ToPort(port).UseDurableOutbox();

                options.UseTaxVisionConflictPolicy();
                options.ApplyStandardFailurePolicies();
            })
            .StartAsync();

    private static int FreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid TenantId => tenantId;
        public bool HasTenant => true;

        public void SetTenant(Guid id) { }
    }

    /// <summary>Falla una vez el guardado que cierra la tarea.</summary>
    private sealed class FailOnceInterceptor : SaveChangesInterceptor
    {
        private int _failures;

        public bool Armed { get; set; }
        public int Failures => Volatile.Read(ref _failures);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            var closing = eventData
                .Context!.ChangeTracker.Entries<TaskItem>()
                .Any(entry => entry.State == EntityState.Modified);
            if (closing && Armed)
            {
                Armed = false;
                Interlocked.Increment(ref _failures);
                throw new TimeoutException("Simulated SQL timeout.");
            }
            return ValueTask.FromResult(result);
        }
    }
}

/// <summary>Lo que de verdad cruzó el puerto, por tarea.</summary>
public static class TaskUnblockedProbeHandler
{
    private static readonly ConcurrentQueue<Guid> Received = new();

    public static int CountFor(Guid taskId) => Received.Count(id => id == taskId);

    public static void Handle(TaskUnblockedIntegrationEvent message) => Received.Enqueue(message.TaskId);
}
