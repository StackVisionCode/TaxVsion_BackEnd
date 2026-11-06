using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tasks.Application.Series;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks.Events;
using TaxVision.Tasks.Infrastructure.Persistence;
using TaxVision.Tasks.Infrastructure.Persistence.Repositories;
using TaxVision.Tasks.Tests.Series;
using Wolverine.EntityFrameworkCore;

namespace TaxVision.Tasks.Tests.Integration;

/// <summary>
/// Contra SQL Server local: un conflicto al guardar una serie no revierte la de al lado. Tenant
/// propio, el barrido solo ve ese tenant y se limpia al final.
/// </summary>
public sealed class SeriesMaterializationIntegrationTests : IAsyncLifetime
{
    private static readonly string ConnectionString =
        new ConfigurationBuilder()
            .AddUserSecrets("12c547c3-e9a6-462e-82c4-bc6f5b0d4e41")
            .AddEnvironmentVariables()
            .Build()
            .GetConnectionString("Default")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Default en los user-secrets de Tasks.Api.");

    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task ConflictInOneSeries_DoesNotRollBackOthers()
    {
        var failing = ResumeTaskSeriesHandlerTests.NewSeries(_tenantId);
        var healthy = ResumeTaskSeriesHandlerTests.NewSeries(_tenantId);
        await using (var seed = NewContext())
        {
            seed.TaskSeries.AddRange(failing, healthy);
            await seed.SaveChangesAsync();
        }

        var recorders = new List<RecordingDbContextOutbox>();
        var services = BuildServices(new FailForSeries(failing.Id), recorders);

        await SeriesMaterializationJobTests.InvokeRunOnceAsync(services);

        await using var check = NewContext();
        var tasks = await check.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == _tenantId).ToListAsync();
        var instance = Assert.Single(tasks);
        Assert.Equal(healthy.Id, instance.SeriesId);
        var series = await check.TaskSeries.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ToListAsync();
        Assert.Null(series.Single(s => s.Id == failing.Id).OpenInstanceId);
        Assert.Equal(instance.Id, series.Single(s => s.Id == healthy.Id).OpenInstanceId);

        // Solo la serie confirmada deja su evento de alta para el outbox; la del conflicto, nada.
        var born = recorders.SelectMany(r => r.DomainEvents).OfType<TaskCreatedDomainEvent>().ToList();
        Assert.Equal(instance.Id, Assert.Single(born).TaskId);
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await using var db = NewContext();
        await db.Tasks.IgnoreQueryFilters().Where(t => t.TenantId == _tenantId).ExecuteDeleteAsync();
        await db.TaskSeries.IgnoreQueryFilters().Where(s => s.TenantId == _tenantId).ExecuteDeleteAsync();
    }

    private ServiceProvider BuildServices(IInterceptor interceptor, List<RecordingDbContextOutbox> recorders) =>
        new ServiceCollection()
            .AddSingleton<ITenantContext, NoTenantContext>()
            .AddDbContext<TasksDbContext>(o => o.UseSqlServer(ConnectionString).AddInterceptors(interceptor))
            .AddScoped<IUnitOfWork>(sp => sp.GetRequiredService<TasksDbContext>())
            .AddScoped(sp =>
            {
                var (outbox, recorder) = RecordingDbContextOutbox.Create(sp.GetRequiredService<TasksDbContext>());
                recorders.Add(recorder);
                return outbox;
            })
            .AddSingleton<ICorrelationContext>(new NoOpCorrelationContext())
            .AddScoped<ITaskSeriesRepository>(sp => new OwnTenantSeries(
                sp.GetRequiredService<TasksDbContext>(),
                _tenantId
            ))
            .AddScoped<ITaskSeriesMaterializer>(sp => new TaskSeriesMaterializer(
                new TaskRepository(sp.GetRequiredService<TasksDbContext>()),
                sp.GetRequiredService<ITaskSeriesRepository>()
            ))
            .BuildServiceProvider();

    private static TasksDbContext NewContext() =>
        new(
            new DbContextOptionsBuilder<TasksDbContext>().UseSqlServer(ConnectionString).Options,
            new NoTenantContext()
        );

    // El barrido real cruza tenants; acá solo ve el de la prueba para no tocar series ajenas.
    internal sealed class OwnTenantSeries(TasksDbContext context, Guid tenantId) : ITaskSeriesRepository
    {
        private readonly TaskSeriesRepository _inner = new(context);

        public void Add(TaskSeries series) => _inner.Add(series);

        public Task<Result<TaskSeries>> GetByIdAsync(Guid tenant, Guid seriesId, CancellationToken ct = default) =>
            _inner.GetByIdAsync(tenant, seriesId, ct);

        public Task<IReadOnlyList<TaskSeries>> ListAsync(
            Guid tenant,
            SeriesStatus? status,
            CancellationToken ct = default
        ) => _inner.ListAsync(tenant, status, ct);

        public async Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
            int take,
            CancellationToken ct = default
        ) =>
            await context
                .TaskSeries.IgnoreQueryFilters()
                .Where(s => s.TenantId == tenantId && s.Status == SeriesStatus.Active && s.OpenInstanceId == null)
                .OrderBy(s => s.CreatedAtUtc)
                .Take(take)
                .ToListAsync(ct);
    }

    // Simula una colisión de rowversion solo al guardar la serie indicada.
    private sealed class FailForSeries(Guid seriesId) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) =>
            eventData.Context!.ChangeTracker.Entries<TaskSeries>().Any(e => e.Entity.Id == seriesId)
                ? ValueTask.FromException<InterceptionResult<int>>(new DbUpdateConcurrencyException("conflict"))
                : ValueTask.FromResult(result);
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid TenantId => Guid.Empty;
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
