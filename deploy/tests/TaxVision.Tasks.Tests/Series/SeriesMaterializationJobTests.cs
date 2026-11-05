using System.Reflection;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Infrastructure.Jobs;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Tests.Series;

// El barrido de series anuncia la instancia por el outbox y solo la suelta si el guardado confirma.
public sealed class SeriesMaterializationJobTests
{
    [Fact]
    public async Task MaterializedInstance_PublishesReminderThroughOutbox()
    {
        var databaseName = await SeedAsync();
        await using var db = NewContext(databaseName);
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        await RunOnceAsync(db, outbox);

        Assert.Empty(recorder.Pending);
        Assert.Equal(1, recorder.Saves);
        var reminder = Assert.Single(recorder.Flushed.OfType<ReminderRequestedIntegrationEvent>());
        await using var check = NewContext(databaseName);
        var instance = Assert.Single(await check.Tasks.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(instance.Id, reminder.TargetId);
    }

    [Fact]
    public async Task ConflictOnSave_ReleasesNothing()
    {
        var databaseName = await SeedAsync();
        await using var db = NewContext(
            databaseName,
            new FailingSaveInterceptor(new DbUpdateConcurrencyException("conflict"))
        );
        var (outbox, recorder) = RecordingDbContextOutbox.Create(db);

        await RunOnceAsync(db, outbox);

        Assert.Equal(0, recorder.Saves);
        Assert.Empty(recorder.Flushed);
        await using var check = NewContext(databaseName);
        Assert.Empty(await check.Tasks.IgnoreQueryFilters().ToListAsync());
    }

    internal static async Task RunOnceAsync(TasksDbContext db, IDbContextOutbox<TasksDbContext> outbox)
    {
        var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IUnitOfWork>(db)
            .AddSingleton(outbox)
            .AddSingleton<ICorrelationContext>(new NoOpCorrelationContext())
            .AddScoped<ITaskSeriesRepository>(sp => new TaskSeriesRepository(sp.GetRequiredService<TasksDbContext>()))
            .AddScoped<ITaskSeriesMaterializer>(sp => new TaskSeriesMaterializer(
                new TaskRepository(sp.GetRequiredService<TasksDbContext>()),
                sp.GetRequiredService<ITaskSeriesRepository>()
            ))
            .BuildServiceProvider();

        await InvokeRunOnceAsync(services);
    }

    internal static Task InvokeRunOnceAsync(IServiceProvider services)
    {
        var job = new SeriesMaterializationJob(services, NullLogger<SeriesMaterializationJob>.Instance);
        var method = typeof(SeriesMaterializationJob).GetMethod(
            "RunOnceAsync",
            BindingFlags.Instance | BindingFlags.NonPublic
        )!;
        return (Task)method.Invoke(job, [CancellationToken.None])!;
    }

    private static async Task<string> SeedAsync()
    {
        var databaseName = Guid.NewGuid().ToString();
        await using var db = NewContext(databaseName);
        db.TaskSeries.Add(ResumeTaskSeriesHandlerTests.NewSeries());
        await db.SaveChangesAsync();
        return databaseName;
    }

    private static TasksDbContext NewContext(string databaseName, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>()
                .UseInMemoryDatabase(databaseName)
                .AddInterceptors(interceptors)
                .Options,
            new NoTenantContext()
        );

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
