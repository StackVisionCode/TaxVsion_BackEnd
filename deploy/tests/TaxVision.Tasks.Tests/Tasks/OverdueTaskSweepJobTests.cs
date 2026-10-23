using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Tasks;
using TaxVision.Tasks.Domain.ValueObjects;
using TaxVision.Tasks.Infrastructure.Jobs;
using TaxVision.Tasks.Infrastructure.Persistence;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Tests.Tasks;

// La marca y el aviso van en la misma transacción: una colisión no deja avisos sueltos ni marcas sin aviso.
public sealed class OverdueTaskSweepJobTests
{
    private const string JobType = "TaxVision.Tasks.Infrastructure.Jobs.OverdueTaskSweepJob";
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 4, 20, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime DueAt = new(2026, 4, 15, 16, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Overdue_notices_leave_through_the_outbox_once_the_marks_are_saved()
    {
        var databaseName = await SeedAsync(2);
        await using var db = NewContext(databaseName);
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingTaskMetrics();

        var announced = await AnnounceChunkAsync(db, outbox, metrics);

        Assert.Equal(2, announced);
        Assert.Equal(1, recorder.Saves);
        Assert.Empty(recorder.Pending);
        Assert.Equal(2, recorder.Flushed.OfType<TaskOverdueIntegrationEvent>().Count());
        Assert.Equal(2, metrics.Overdue);
        await using var check = NewContext(databaseName);
        Assert.All(
            await check.Tasks.IgnoreQueryFilters().ToListAsync(),
            task => Assert.NotNull(task.OverdueNotifiedAtUtc)
        );
    }

    [Fact]
    public async Task A_concurrency_conflict_announces_nothing_and_leaves_the_tasks_unmarked()
    {
        var databaseName = await SeedAsync(2);
        await using var db = NewContext(
            databaseName,
            new FailingSaveInterceptor(new DbUpdateConcurrencyException("conflict"))
        );
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);
        var metrics = new RecordingTaskMetrics();

        var announced = await AnnounceChunkAsync(db, outbox, metrics);

        Assert.Equal(0, announced);
        Assert.Equal(0, recorder.Saves);
        Assert.Empty(recorder.Flushed);
        Assert.Equal(0, metrics.Overdue);
        await using var check = NewContext(databaseName);
        Assert.All(
            await check.Tasks.IgnoreQueryFilters().ToListAsync(),
            task => Assert.Null(task.OverdueNotifiedAtUtc)
        );
    }

    [Fact]
    public async Task Tasks_already_announced_neither_publish_nor_save()
    {
        var databaseName = await SeedAsync(1, alreadyNotified: true);
        await using var db = NewContext(databaseName);
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        var announced = await AnnounceChunkAsync(db, outbox, new RecordingTaskMetrics());

        Assert.Equal(0, announced);
        Assert.Equal(0, recorder.Saves);
        Assert.Empty(recorder.Pending);
    }

    private static async Task<int> AnnounceChunkAsync(
        TasksDbContext db,
        IDbContextOutbox<TasksDbContext> outbox,
        RecordingTaskMetrics metrics
    )
    {
        var tasks = await db.Tasks.IgnoreQueryFilters().ToListAsync();
        var services = new ServiceCollection()
            .AddSingleton(OverdueTasks.Create(tasks))
            .AddSingleton(outbox)
            .AddSingleton<ICorrelationContext>(new NoOpCorrelationContext())
            .AddSingleton<ITaskMetrics>(metrics)
            .BuildServiceProvider();

        var type = typeof(TasksDbContext).Assembly.GetType(JobType, throwOnError: true)!;
        var job = Activator.CreateInstance(
            type,
            services.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OverdueTaskSweepOptions()),
            Activator.CreateInstance(typeof(NullLogger<>).MakeGenericType(type))
        )!;
        var method = type.GetMethod("AnnounceChunkAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        return await (Task<int>)method.Invoke(job, [Now, CancellationToken.None])!;
    }

    private static async Task<string> SeedAsync(int count, bool alreadyNotified = false)
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var db = NewContext(databaseName);
        for (var index = 0; index < count; index++)
        {
            var task = NewTask();
            if (alreadyNotified)
                task.MarkOverdueNotified(Now.AddHours(-1));
            db.Tasks.Add(task);
        }
        await db.SaveChangesAsync();
        return databaseName;
    }

    private static TaskItem NewTask() =>
        TaskItem
            .Create(
                Guid.NewGuid(),
                UserId,
                TaskTitle.Create("Transmitir el e-file").Value,
                null,
                TaskPriority.High,
                TaskReference.None,
                DueDate.Create(DueAt, "America/New_York", true).Value,
                null,
                UserId,
                DueAt.AddDays(-30)
            )
            .Value;

    private static TasksDbContext NewContext(string databaseName, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(interceptors)
                .Options,
            new NoTenantContext()
        );

    // Solo responde el listado de vencidas; el resto del contrato no lo usa el job.
    public class OverdueTasks : DispatchProxy
    {
        private IReadOnlyList<TaskItem> _tasks = [];

        internal static ITaskRepository Create(IReadOnlyList<TaskItem> tasks)
        {
            var repository = Create<ITaskRepository, OverdueTasks>();
            ((OverdueTasks)(object)repository)._tasks = tasks;
            return repository;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(ITaskRepository.ListOverdueAsync)
                ? Task.FromResult(_tasks)
                : throw new NotSupportedException(targetMethod?.Name);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
