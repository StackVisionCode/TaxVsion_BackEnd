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

    public List<IReadOnlyList<ImportedDirectoryEntry>> ImportedUpserts { get; } = [];

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

    // Misma regla que el MERGE: crea la que falta y solo actualiza si no hay un cambio más nuevo.
    public Task UpsertImportedAsync(
        Guid tenantId,
        IReadOnlyList<ImportedDirectoryEntry> entries,
        DateTime observedAtUtc,
        CancellationToken ct = default
    )
    {
        ImportedUpserts.Add(entries);
        foreach (var imported in entries)
        {
            if (_entries.TryGetValue((tenantId, imported.CustomerId), out var existing))
                existing.ApplyIfNewer(
                    imported.DisplayName,
                    imported.Status,
                    observedAtUtc,
                    imported.PrimaryEmailNormalized
                );
            else
                _entries[(tenantId, imported.CustomerId)] = CustomerDirectoryEntry.Create(
                    tenantId,
                    imported.CustomerId,
                    imported.DisplayName,
                    imported.Status,
                    observedAtUtc,
                    imported.PrimaryEmailNormalized
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
    public bool Needed { get; init; }

    public Task<bool> NeedsBackfillAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(Needed);

    public Task BackfillAsync(Guid tenantId, CancellationToken ct = default)
    {
        Calls.Add(tenantId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryTaskRepository(params TaskItem[] seed) : ITaskRepository
{
    // Lo sembrado ya está guardado: sus eventos de dominio ya se despacharon.
    private readonly List<TaskItem> _tasks = [.. seed.Select(Persisted)];

    private static TaskItem Persisted(TaskItem task)
    {
        task.ClearDomainEvents();
        return task;
    }

    public IReadOnlyList<TaskItem> Stored => _tasks;

    public void Add(TaskItem task) => _tasks.Add(task);

    public void Remove(TaskItem task) => _tasks.Remove(task);

    public Dictionary<Guid, byte[]> ExpectedVersions { get; } = [];

    public Task<IReadOnlyList<TaskItem>> ListBySeriesAsync(
        Guid tenantId,
        Guid seriesId,
        int take,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks
                .Where(t => t.TenantId == tenantId && t.SeriesId == seriesId)
                .OrderByDescending(t => t.OccurrenceNumber)
                .Take(take),
        ]);

    public void ExpectVersion(TaskItem task, byte[] rowVersion) => ExpectedVersions[task.Id] = rowVersion;

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
        var task = _tasks.FirstOrDefault(t => t.TenantId == tenantId && t.Id == taskId && !t.IsInTrash);
        return Task.FromResult(task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task));
    }

    public Task<Result<TaskItem>> GetIncludingTrashAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
    {
        var task = _tasks.FirstOrDefault(t => t.TenantId == tenantId && t.Id == taskId);
        return Task.FromResult(task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task));
    }

    public Task<IReadOnlyList<TaskItem>> ListTrashBranchAsync(
        Guid tenantId,
        Guid trashRootId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks.Where(t => t.TenantId == tenantId && t.TrashRootId == trashRootId),
        ]);

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
        Guid? assignedToUserId = null,
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
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public TaskQueryFilter? LastCalendarFilter { get; private set; }

    public Task<IReadOnlyList<TaskItem>> ListForCalendarAsync(
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        TaskQueryFilter filter,
        int take,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    )
    {
        LastCalendarFilter = filter;
        return Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks
                .Where(t =>
                    t.TenantId == tenantId && t.Due is { } due && due.DueAtUtc >= fromUtc && due.DueAtUtc <= toUtc
                )
                .OrderBy(t => t.Due!.DueAtUtc)
                .Take(take),
        ]);
    }

    public Task<IReadOnlyList<TaskItem>> ListByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks.Where(t => t.TenantId == tenantId && taskIds.Contains(t.Id)),
        ]);

    public async Task<IReadOnlyList<TaskItem>> ListByIdsForReadAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var visible = new List<TaskItem>();
        foreach (var id in taskIds)
            if ((await GetByIdForReadAsync(tenantId, id, assignedToUserId, ct)).IsSuccess)
                visible.Add(_tasks.First(t => t.Id == id));
        return visible;
    }

    // Sube por ParentTaskId como el repositorio real (sin incluir la propia tarea).
    public Task<IReadOnlyList<Guid>> GetAncestorIdsAsync(Guid tenantId, Guid taskId, CancellationToken ct = default)
    {
        var ancestors = new List<Guid>();
        var current = _tasks.FirstOrDefault(t => t.TenantId == tenantId && t.Id == taskId);
        while (current?.ParentTaskId is { } parentId)
        {
            ancestors.Add(parentId);
            current = _tasks.FirstOrDefault(t => t.TenantId == tenantId && t.Id == parentId);
        }

        return Task.FromResult<IReadOnlyList<Guid>>(ancestors);
    }

    public Task<PagedResult<TaskItem>> ListForAssigneeAsync(
        Guid tenantId,
        Guid assigneeUserId,
        TaskItemStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var matched = _tasks
            .Where(t =>
                t.TenantId == tenantId
                && t.AssigneeUserId == assigneeUserId
                && (
                    status is { } wanted
                        ? t.Status == wanted
                        : t.Status
                            is TaskItemStatus.NotStarted
                                or TaskItemStatus.InProgress
                                or TaskItemStatus.WaitingOnClient
                )
            )
            .ToList();
        var pageItems = matched.Skip((page - 1) * size).Take(size).ToList();
        return Task.FromResult(new PagedResult<TaskItem>(pageItems, page, size, matched.Count));
    }

    public Task<int> CountForAssigneeAsync(
        Guid tenantId,
        Guid assigneeUserId,
        TaskItemStatus? status,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            _tasks.Count(t =>
                t.TenantId == tenantId
                && t.AssigneeUserId == assigneeUserId
                && (
                    status is { } wanted
                        ? t.Status == wanted
                        : t.Status
                            is TaskItemStatus.NotStarted
                                or TaskItemStatus.InProgress
                                or TaskItemStatus.WaitingOnClient
                )
            )
        );

    public Task<IReadOnlyList<TaskItem>> ListWithRunningTimersOfAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskItem>>([
            .. _tasks.Where(t => t.TenantId == tenantId && t.Timers.Any(x => x.UserId == userId && x.IsRunning)),
        ]);

    public Task<int> CountRunningTimersOfAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        Task.FromResult(
            _tasks
                .Where(t => t.TenantId == tenantId)
                .SelectMany(t => t.Timers)
                .Count(x => x.UserId == userId && x.IsRunning)
        );

    public Task<PagedResult<TaskItem>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int? taxYear,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<PagedResult<TaskItem>> ListWaitingOnClientAsync(
        Guid tenantId,
        int page,
        int size,
        Guid? assignedToUserId = null,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<TaskItem>> ListOverdueAsync(DateTime nowUtc, int take, CancellationToken ct = default) =>
        throw new NotImplementedException();

    // Misma regla de visibilidad que el repositorio real (P2).
    public HashSet<(Guid CustomerId, Guid UserId)> CustomerAssignments { get; } = [];

    public Task<Result<TaskItem>> GetByIdForReadAsync(
        Guid tenantId,
        Guid taskId,
        Guid? assignedToUserId,
        CancellationToken ct = default
    )
    {
        var task = _tasks.FirstOrDefault(t =>
            t.TenantId == tenantId
            && t.Id == taskId
            && (
                assignedToUserId is not { } user
                || t.Reference.CustomerId is null
                || t.AssigneeUserId == user
                || t.CreatedByUserId == user
                || CustomerAssignments.Contains((t.Reference.CustomerId.Value, user))
            )
        );
        return Task.FromResult(task is null ? Result.Failure<TaskItem>(TaskErrors.NotFound) : Result.Success(task));
    }
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

    public Task<IReadOnlyList<TaskDependency>> ListTouchingAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> taskIds,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskDependency>>([
            .. _edges.Where(d =>
                d.TenantId == tenantId && (taskIds.Contains(d.TaskId) || taskIds.Contains(d.DependsOnTaskId))
            ),
        ]);

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
    public Task<Result> CanReopenAsync(TaskItem task, CancellationToken ct = default) =>
        Task.FromResult(Result.Success());

    public Task RegisterReopenedAsync(TaskItem task, CancellationToken ct = default) => Task.CompletedTask;

    public int InstancesClosed { get; private set; }

    public Task<Result<TaskItem>> MaterializeNextAsync(
        TaskSeries series,
        DateTime? lastDueUtc,
        DateTime? completedAtUtc,
        CancellationToken ct = default
    ) => Task.FromResult(Result.Failure<TaskItem>(TaskErrors.Series.NoFurtherOccurrence));

    public Task<Result<TaskItem>> MaterializeFirstAsync(TaskSeries series, CancellationToken ct = default) =>
        MaterializeNextAsync(series, null, null, ct);

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

/// <summary>Series en memoria: el pendiente es la activa sin instancia abierta, como en SQL.</summary>
internal sealed class InMemoryTaskSeriesRepository(params TaskSeries[] seed) : ITaskSeriesRepository
{
    public List<TaskSeries> Stored { get; } = [.. seed];

    public void Add(TaskSeries series) => Stored.Add(series);

    public void ExpectVersion(TaskSeries series, byte[] rowVersion) { }

    public Task<Result<TaskSeries>> GetByIdAsync(Guid tenantId, Guid seriesId, CancellationToken ct = default)
    {
        var found = Stored.FirstOrDefault(s => s.TenantId == tenantId && s.Id == seriesId);
        return Task.FromResult(
            found is null ? Result.Failure<TaskSeries>(TaskErrors.Series.NotFound) : Result.Success(found)
        );
    }

    public Task<IReadOnlyList<TaskSeries>> ListAsync(
        Guid tenantId,
        SeriesStatus? status,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskSeries>>(
            Stored.Where(s => s.TenantId == tenantId && (status is null || s.Status == status)).ToList()
        );

    /// <summary>Clientes asignados (cliente, usuario) para la visibilidad.</summary>
    public HashSet<(Guid CustomerId, Guid UserId)> CustomerAssignments { get; } = [];

    public Task<(IReadOnlyList<TaskSeries> Items, int Total)> ListPageAsync(
        SeriesListFilter filter,
        CancellationToken ct = default
    )
    {
        var all = Stored
            .Where(s => s.TenantId == filter.TenantId && (filter.Status is null || s.Status == filter.Status))
            .Where(s =>
                filter.OnlyFor is not { } mine || s.EffectiveOwnerUserId == mine || s.Blueprint.AssigneeUserId == mine
            )
            .Where(s =>
                filter.VisibleTo is not { } viewer
                || s.EffectiveOwnerUserId == viewer
                || s.Blueprint.AssigneeUserId == viewer
                || s.Blueprint.Reference.CustomerId is null
                || CustomerAssignments.Contains((s.Blueprint.Reference.CustomerId.Value, viewer))
            )
            .OrderByDescending(s => s.CreatedAtUtc)
            .ToList();
        IReadOnlyList<TaskSeries> page = [.. all.Skip((filter.Page - 1) * filter.Size).Take(filter.Size)];
        return Task.FromResult((page, all.Count));
    }

    public Task<IReadOnlyList<TaskSeries>> ListPendingMaterializationAsync(
        int take,
        DateTime nowUtc,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaskSeries>>(
            Stored
                .Where(s =>
                    s.Status == SeriesStatus.Active
                    && s.OpenInstanceId == null
                    && (s.NextMaterializationAtUtc == null || s.NextMaterializationAtUtc <= nowUtc)
                )
                .Take(take)
                .ToList()
        );
}

/// <summary>Historial en memoria, de lo más nuevo a lo más viejo como el repositorio real.</summary>
internal sealed class InMemoryTaskActivityRepository(params TaxVision.Tasks.Domain.Activity.TaskActivity[] seed)
    : TaxVision.Tasks.Application.Activity.Abstractions.ITaskActivityRepository
{
    public List<TaxVision.Tasks.Domain.Activity.TaskActivity> Stored { get; } = [.. seed];

    public void Add(TaxVision.Tasks.Domain.Activity.TaskActivity activity) => Stored.Add(activity);

    public Task<IReadOnlyList<TaxVision.Tasks.Domain.Activity.TaskActivity>> ListAsync(
        Guid tenantId,
        Guid taskId,
        DateTime? before,
        int take,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaxVision.Tasks.Domain.Activity.TaskActivity>>(
            Stored
                .Where(a =>
                    a.TenantId == tenantId && a.TaskId == taskId && (before is null || a.OccurredAtUtc < before)
                )
                .OrderByDescending(a => a.OccurredAtUtc)
                .Take(take)
                .ToList()
        );
}

/// <summary>Zona fija de la oficina para las pruebas que dependen del día civil.</summary>
internal sealed class FixedOfficeZone(string timeZoneId) : IOfficeTimeZone
{
    public Task<BuildingBlocks.TimeZones.ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(BuildingBlocks.TimeZones.ResolvedTimeZone.Resolve(timeZoneId));
}

/// <summary>Estados en memoria; las tareas se miran en el repositorio de tareas que se le pase.</summary>
internal sealed class InMemoryWorkflowStatusRepository(params TaxVision.Tasks.Domain.Statuses.WorkflowStatus[] seed)
    : TaxVision.Tasks.Application.Statuses.Abstractions.IWorkflowStatusRepository
{
    private static readonly TaskItemStatus[] Open =
    [
        TaskItemStatus.NotStarted,
        TaskItemStatus.InProgress,
        TaskItemStatus.WaitingOnClient,
    ];

    public List<TaxVision.Tasks.Domain.Statuses.WorkflowStatus> Stored { get; } = [.. seed];
    public List<TaskItem> Tasks { get; } = [];

    public void Add(TaxVision.Tasks.Domain.Statuses.WorkflowStatus status) => Stored.Add(status);

    public void Remove(TaxVision.Tasks.Domain.Statuses.WorkflowStatus status) => Stored.Remove(status);

    private IEnumerable<TaxVision.Tasks.Domain.Statuses.WorkflowStatus> Of(Guid tenantId) =>
        Stored.Where(s => s.TenantId == tenantId);

    public Task<Result<TaxVision.Tasks.Domain.Statuses.WorkflowStatus>> GetByIdAsync(
        Guid tenantId,
        Guid statusId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Of(tenantId).FirstOrDefault(s => s.Id == statusId) is { } found
                ? Result.Success(found)
                : Result.Failure<TaxVision.Tasks.Domain.Statuses.WorkflowStatus>(TaskErrors.Label.NotFound)
        );

    public Task<TaxVision.Tasks.Domain.Statuses.WorkflowStatus?> GetByCodeAsync(
        Guid tenantId,
        TaxVision.Tasks.Domain.ValueObjects.TaskLabelCode code,
        CancellationToken ct = default
    ) => Task.FromResult(Of(tenantId).FirstOrDefault(s => s.Code == code));

    public Task<bool> CodeExistsAsync(
        Guid tenantId,
        TaxVision.Tasks.Domain.ValueObjects.TaskLabelCode code,
        Guid? excludingStatusId,
        CancellationToken ct = default
    ) => Task.FromResult(Of(tenantId).Any(s => s.Code == code && s.Id != excludingStatusId));

    public Task<IReadOnlyList<TaxVision.Tasks.Domain.Statuses.WorkflowStatus>> ListAsync(
        Guid tenantId,
        bool includeArchived = false,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<TaxVision.Tasks.Domain.Statuses.WorkflowStatus>>([
            .. Of(tenantId).Where(s => includeArchived || !s.IsArchived).OrderBy(s => s.SortOrder),
        ]);

    public Task<IReadOnlySet<string>> ListCodesAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlySet<string>>(Of(tenantId).Select(s => s.Code.Value).ToHashSet());

    public Task<bool> NameTakenAsync(
        Guid tenantId,
        string displayName,
        Guid? excludingStatusId,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Of(tenantId)
                .Any(s =>
                    !s.IsArchived
                    && string.Equals(s.DisplayName, displayName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && s.Id != excludingStatusId
                )
        );

    public Task<int> CountActiveAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Of(tenantId).Count(s => !s.IsArchived));

    public Task<int> CountOpenTasksAsync(Guid tenantId, Guid statusId, CancellationToken ct = default) =>
        Task.FromResult(
            Tasks.Count(t => t.TenantId == tenantId && t.WorkflowStatusId == statusId && Open.Contains(t.Status))
        );

    public Task<
        IReadOnlyList<TaxVision.Tasks.Application.Statuses.Abstractions.OpenTaskTally>
    > CountOpenTasksByCodeAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TaxVision.Tasks.Application.Statuses.Abstractions.OpenTaskTally>>([
            .. Tasks
                .Where(t => t.TenantId == tenantId && Open.Contains(t.Status))
                .GroupBy(t => (t.Status, t.WorkflowStatusCode))
                .Select(g => new TaxVision.Tasks.Application.Statuses.Abstractions.OpenTaskTally(
                    g.Key.Status,
                    g.Key.WorkflowStatusCode,
                    g.Count()
                )),
        ]);

    public Task<bool> IsUsedAsync(Guid tenantId, Guid statusId, CancellationToken ct = default) =>
        Task.FromResult(Tasks.Any(t => t.TenantId == tenantId && t.WorkflowStatusId == statusId));

    public Task<int> MoveOpenTasksAsync(
        Guid tenantId,
        Guid fromStatusId,
        TaxVision.Tasks.Domain.Statuses.WorkflowStatusRef target,
        CancellationToken ct = default
    )
    {
        var moving = Tasks
            .Where(t => t.TenantId == tenantId && t.WorkflowStatusId == fromStatusId && t.Status == target.Category)
            .ToList();
        foreach (var task in moving)
            task.MoveTo(target, TaskMoveRequest.None, Guid.Empty, DateTime.UtcNow);
        return Task.FromResult(moving.Count);
    }

    public byte[]? ExpectedVersion { get; private set; }

    public void ExpectVersion(TaxVision.Tasks.Domain.Statuses.WorkflowStatus status, byte[] rowVersion) =>
        ExpectedVersion = rowVersion;

    public Task<int> ReleaseOpenTasksFromArchivedAsync(int take, CancellationToken ct = default) => Task.FromResult(0);
}

/// <summary>Quien guarda solo ve estos clientes.</summary>
internal sealed class OnlyTheseCustomers(params Guid[] visible) : ITaskCustomerAccess
{
    public Task<bool> CanLinkAsync(
        Guid tenantId,
        Guid userId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct
    ) => Task.FromResult(canViewAll || visible.Contains(customerId));
}

/// <summary>Sin recorte: la visibilidad apagada o quien ve a todos.</summary>
internal sealed class EveryCustomer : ITaskCustomerAccess
{
    public Task<bool> CanLinkAsync(
        Guid tenantId,
        Guid userId,
        bool canViewAll,
        Guid customerId,
        CancellationToken ct
    ) => Task.FromResult(true);
}
