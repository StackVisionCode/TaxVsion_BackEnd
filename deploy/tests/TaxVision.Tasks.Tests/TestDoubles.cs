using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.Application.Backfill.Abstractions;
using TaxVision.Tasks.Application.Common.Abstractions;
using TaxVision.Tasks.Application.Dependencies.Abstractions;
using TaxVision.Tasks.Application.Projections.Abstractions;
using TaxVision.Tasks.Application.Series.Abstractions;
using TaxVision.Tasks.Application.Tasks.Abstractions;
using TaxVision.Tasks.Domain.Backfill;
using TaxVision.Tasks.Domain.Dependencies;
using TaxVision.Tasks.Domain.Projections;
using TaxVision.Tasks.Domain.Series;
using TaxVision.Tasks.Domain.Tasks;

namespace TaxVision.Tasks.Tests;

internal sealed class RecordingUnitOfWork : IUnitOfWork
{
    public int SaveCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveCount++;
        return Task.FromResult(0);
    }
}

internal sealed class NoOpCorrelationContext : ICorrelationContext
{
    public string CorrelationId => "test";

    public void Set(string correlationId) { }

    public IDisposable Push(string correlationId) => new NoOpScope();

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose() { }
    }
}

internal sealed class InMemoryCustomerDirectoryRepository(params CustomerDirectoryEntry[] seed)
    : ICustomerDirectoryRepository
{
    private readonly Dictionary<(Guid TenantId, Guid CustomerId), CustomerDirectoryEntry> _entries = seed.ToDictionary(
        e => (e.TenantId, e.CustomerId)
    );

    public List<(Guid TenantId, IReadOnlyCollection<Guid> CustomerIds)> BulkUpserts { get; } = [];

    public IReadOnlyCollection<CustomerDirectoryEntry> Entries => _entries.Values;

    public Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(_entries.ContainsKey((tenantId, customerId)));

    public Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(_entries.GetValueOrDefault((tenantId, customerId))?.DisplayName);

    public Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => Task.FromResult(_entries.GetValueOrDefault((tenantId, customerId)));

    public Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default)
    {
        _entries[(entry.TenantId, entry.CustomerId)] = entry;
        return Task.CompletedTask;
    }

    public Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        DateTime observedAtUtc,
        CancellationToken ct = default
    )
    {
        BulkUpserts.Add((tenantId, customerIds));
        foreach (var customerId in customerIds)
        {
            if (_entries.ContainsKey((tenantId, customerId)))
                continue;
            _entries[(tenantId, customerId)] = CustomerDirectoryEntry.Create(
                tenantId,
                customerId,
                null,
                CustomerDirectoryStatus.Active,
                observedAtUtc
            );
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(int limit, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>(
            _entries.Values.Where(e => e.DisplayName is null).Select(e => e.TenantId).Distinct().Take(limit).ToList()
        );

    public Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    )
    {
        _entries.GetValueOrDefault((tenantId, customerId))?.ApplyDisplayNameIfMissing(displayName);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTenantBackfillStateRepository(params Guid[] seed) : ITenantBackfillStateRepository
{
    private readonly Dictionary<Guid, TenantBackfillState> _states = seed.ToDictionary(
        id => id,
        TenantBackfillState.Create
    );

    public IReadOnlyCollection<Guid> CompletedTenantIds => _states.Keys;

    public Task<TenantBackfillState?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(_states.GetValueOrDefault(tenantId));

    public Task AddAsync(TenantBackfillState entity, CancellationToken ct = default)
    {
        _states[entity.TenantId] = entity;
        return Task.CompletedTask;
    }
}

internal sealed class RecordingBackfillService : ITenantCustomerBackfillService
{
    public List<Guid> Calls { get; } = [];

    public Task EnsureBackfilledAsync(Guid tenantId, CancellationToken ct = default)
    {
        Calls.Add(tenantId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTaskRepository(params TaskItem[] seed) : ITaskRepository
{
    private readonly List<TaskItem> _tasks = [.. seed];

    public void Add(TaskItem task) => _tasks.Add(task);

    public void Remove(TaskItem task) => _tasks.Remove(task);

    public Task<IReadOnlyList<Guid>> ListChildIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> parentTaskIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<Guid>>([
            .. _tasks
                .Where(t => t.TenantId == tenantId && t.ParentTaskId is { } p && parentTaskIds.Contains(p))
                .Select(t => t.Id),
        ]);

    public Task<Result<TaskItem>> GetByIdAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
    {
        var task = _tasks.FirstOrDefault(t => t.TenantId == tenantId && t.Id == taskId);
        return Task.FromResult(task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task));
    }

    // En memoria los timers ya cuelgan del agregado: no hay carga diferida que simular.
    public Task<Result<TaskItem>> GetByIdWithTimersAsync(Guid tenantId, Guid taskId, CancellationToken ct = default) =>
        GetByIdAsync(tenantId, taskId, ct);

    public Task<Result<TaskItem>> GetByIdWithAttachmentsAsync(
        Guid tenantId,
        Guid taskId,
        CancellationToken ct = default
    ) => GetByIdAsync(tenantId, taskId, ct);

    public Task<TaskItem?> GetByAttachmentFileIdAsync(Guid fileId, CancellationToken ct = default) =>
        Task.FromResult(_tasks.FirstOrDefault(t => t.Attachments.Any(a => a.FileId == fileId)));

    public Task<IReadOnlyList<TaskItem>> ListWithAttachmentsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks.Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id)),
        ]);

    public Task<PagedResult<TaskItem>> ListSubtasksAsync(
        Guid tenantId,
        Guid parentTaskId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var children = _tasks.Where(t => t.TenantId == tenantId && t.ParentTaskId == parentTaskId).ToList();
        return Task.FromResult(new PagedResult<TaskItem>(children, page, size, children.Count));
    }

    public Task<PagedResult<TaskItem>> SearchAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TaskItem>> ListForBoardAsync(
        Guid tenantId,
        TaskQueryFilter filter,
        int take,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TaskItem>> ListForCalendarAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        Guid? assigneeUserId,
        int take,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TaskItem>> ListByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks.Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id)),
        ]);

    public Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(Guid tenantId, Guid taskId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Guid>>([]);

    public Task<PagedResult<TaskItem>> ListForAssigneeAsync(
        Guid tenantId,
        Guid assigneeUserId,
        TaskItemStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<PagedResult<TaskItem>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int? taxYear,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<PagedResult<TaskItem>> ListWaitingOnClientAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TaskItem>> ListOverdueAsync(DateTime nowUtc, int take, CancellationToken ct = default) =>
        throw new NotImplementedException();
}

/// <summary>Sólo modela las aristas; el grafo recursivo y el cerrojo no aplican en memoria.</summary>
internal sealed class InMemoryTaskDependencyRepository : ITaskDependencyRepository
{
    private readonly List<TaskDependency> _edges = [];

    public List<CounterMismatch> Mismatches { get; } = [];

    public void Add(TaskDependency dependency) => _edges.Add(dependency);

    public void Remove(TaskDependency dependency) => _edges.Remove(dependency);

    public void Seed(Guid tenantId, Guid taskId, Guid dependsOnTaskId) =>
        _edges.Add(TaskDependency.Create(tenantId, taskId, dependsOnTaskId, Guid.NewGuid(), DateTime.UtcNow).Value);

    public Task<TaskDependency?> GetAsync(
        Guid tenantId,
        Guid taskId,
        Guid dependsOnTaskId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _edges.FirstOrDefault(d =>
                d.TenantId == tenantId && d.TaskId == taskId && d.DependsOnTaskId == dependsOnTaskId
            )
        );

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> LoadUpstreamGraphAsync(
        Guid tenantId,
        Guid startTaskId,
        CancellationToken ct = default
    )
    {
        var graph = _edges
            .Where(d => d.TenantId == tenantId)
            .GroupBy(d => d.TaskId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)[.. g.Select(d => d.DependsOnTaskId)]);

        return Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(graph);
    }

    public Task LockTenantEdgesAsync(Guid tenantId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<int> CountOpenBlockersAsync(Guid tenantId, Guid taskId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> ListSuccessorIdsAsync(
        Guid tenantId,
        Guid dependsOnTaskId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<Guid>>([
            .. _edges.Where(d => d.TenantId == tenantId && d.DependsOnTaskId == dependsOnTaskId).Select(d => d.TaskId),
        ]);

    public Task<IReadOnlyList<CounterMismatch>> ListCounterMismatchesAsync(int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CounterMismatch>>([.. Mismatches.Take(take)]);
}

internal sealed class ImmediateTransactionalScope : ITransactionalScope
{
    public int CommitCount { get; private set; }

    public Task<ITransactionalScopeHandle> BeginAsync(CancellationToken ct = default) =>
        Task.FromResult<ITransactionalScopeHandle>(new Handle(this));

    private sealed class Handle(ImmediateTransactionalScope owner) : ITransactionalScopeHandle
    {
        public Task CommitAsync(CancellationToken ct = default)
        {
            owner.CommitCount++;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>
/// Cuenta materializaciones sin tocar la base: los handlers de cierre sólo tienen que llamarla, y
/// cuántas ocurrencias salen de una regla ya lo cubre <c>TaskSeriesTests</c>.
/// </summary>
public sealed class RecordingSeriesMaterializer : ITaskSeriesMaterializer
{
    public int InstancesClosed { get; private set; }

    public Task<Result<TaskItem>> MaterializeNextAsync(
        TaskSeries series,
        DateTime? lastDueUtc,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Failure<TaskItem>(TaskErrors.Series.NoFurtherOccurrence));

    public Task<TaskItem?> ApplyInstanceClosedAsync(
        TaskItem task,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    )
    {
        InstancesClosed++;
        return Task.FromResult<TaskItem?>(null);
    }
}

/// <summary>Cuenta en memoria: los tests miran qué se midió, no cómo se exporta.</summary>
internal sealed class RecordingTaskMetrics : ITaskMetrics
{
    public int Created { get; private set; }
    public int Completed { get; private set; }
    public int Blocked { get; private set; }
    public int CyclesRejected { get; private set; }
    public int ReconciliationCorrections { get; private set; }
    public int Overdue { get; private set; }

    public void RecordCreated(bool hasCustomer) => Created++;

    public void RecordCompleted(bool hasCustomer) => Completed++;

    public void RecordBlocked() => Blocked++;

    public void RecordDependencyCycleRejected() => CyclesRejected++;

    public void RecordReconciliationCorrections(int count) => ReconciliationCorrections += count;

    public void RecordTimeToCompleteSeconds(double seconds) { }

    public void RecordOverdue(int count) => Overdue += count;
}
