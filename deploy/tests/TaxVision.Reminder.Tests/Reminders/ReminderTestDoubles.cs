using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Reminder.Application.Reminders.Abstractions;
using TaxVision.Reminder.Domain.Reminders;
using TaxVision.Reminder.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Reminder.Tests.Reminders;

/// <summary>
/// Fakes escritos a mano, no una librería de mocking — el repo no tiene ninguna, y para puertos de
/// pocos métodos un fake es más legible que un setup encadenado. Compartidos por los tests de
/// comandos, de disparo y de consumers para que el comportamiento simulado (la carrera del índice
/// único, sobre todo) sea uno solo.
/// </summary>
internal sealed class FakeReminderRepository : IReminderRepository
{
    private static readonly ReminderStatus[] PendingStatuses =
    [
        ReminderStatus.Scheduled,
        ReminderStatus.Fired,
        ReminderStatus.Snoozed,
    ];

    public List<ReminderAggregate> Stored { get; } = [];

    /// <summary>Simula la ventana de la carrera: la consulta previa no ve al ganador.</summary>
    public bool HideFromLookupOnce { get; set; }

    private ReminderAggregate? _pending;

    /// <summary>Deja un recordatorio ya persistido, sin pasar por el alta.</summary>
    internal void Seed(ReminderAggregate reminder) => Stored.Add(reminder);

    public void Add(ReminderAggregate reminder) => _pending = reminder;

    public Task<ReminderAggregate?> FindByRequestKeyAsync(
        Guid tenantId,
        RequestKey requestKey,
        CancellationToken ct = default
    )
    {
        if (HideFromLookupOnce)
        {
            HideFromLookupOnce = false;
            return Task.FromResult<ReminderAggregate?>(null);
        }

        return Task.FromResult(
            Stored.FirstOrDefault(r => r.TenantId == tenantId && r.RequestKey.Value == requestKey.Value)
        );
    }

    public Task<Result<ReminderAggregate>> GetOwnedAsync(
        Guid tenantId,
        Guid userId,
        Guid reminderId,
        CancellationToken ct = default
    )
    {
        var found = Stored.FirstOrDefault(r => r.TenantId == tenantId && r.UserId == userId && r.Id == reminderId);
        return Task.FromResult(
            found is null ? Result.Failure<ReminderAggregate>(ReminderErrors.NotFound) : Result.Success(found)
        );
    }

    public Task<Result<ReminderAggregate>> GetForSchedulerAsync(
        Guid tenantId,
        Guid reminderId,
        CancellationToken ct = default
    )
    {
        var found = Stored.FirstOrDefault(r => r.TenantId == tenantId && r.Id == reminderId);
        return Task.FromResult(
            found is null ? Result.Failure<ReminderAggregate>(ReminderErrors.NotFound) : Result.Success(found)
        );
    }

    public Task<IReadOnlyList<ReminderAggregate>> ListPendingByTargetAsync(
        Guid tenantId,
        ReminderCategory category,
        Guid targetId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<ReminderAggregate>>(
            Stored
                .Where(r =>
                    r.TenantId == tenantId
                    && r.Target.Category == category
                    && r.Target.TargetId == targetId
                    && PendingStatuses.Contains(r.Status)
                )
                .ToList()
        );

    /// <summary>
    /// Es <see cref="IUnitOfWork"/> quien confirma el <c>Add</c> pendiente: sin eso, el <c>catch</c>
    /// de la carrera vería la fila del perdedor ya guardada y el test no probaría nada.
    /// </summary>
    internal void Commit()
    {
        if (_pending is null)
            return;

        var duplicate = Stored.Any(r =>
            r.TenantId == _pending.TenantId && r.RequestKey.Value == _pending.RequestKey.Value
        );
        var pending = _pending;
        _pending = null;

        if (duplicate)
            throw new ConflictException("Persistence.UniqueConstraint", "duplicate");

        Stored.Add(pending);
    }

    public Task<Result<ReminderAggregate>> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        throw new NotSupportedException("Nadie lo usa: todo pasa por GetOwnedAsync o GetForSchedulerAsync.");

    public Task<PagedResult<ReminderAggregate>> ListForUserAsync(
        Guid tenantId,
        Guid userId,
        ReminderStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<PagedResult<ReminderAggregate>> ListUpcomingForUserAsync(
        Guid tenantId,
        Guid userId,
        DateTime fromUtc,
        DateTime toUtc,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotSupportedException();

    public Task<IReadOnlyList<ReminderAggregate>> ListScheduledWithinHorizonAsync(
        DateTime horizonUtc,
        CancellationToken ct = default
    ) => throw new NotSupportedException("Lo usa el job de reconciliación, no los handlers.");

    public IUnitOfWork AsUnitOfWork() => new CommitUnitOfWork(this);

    private sealed class CommitUnitOfWork(FakeReminderRepository repository) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            repository.Commit();
            return Task.FromResult(1);
        }
    }
}

internal sealed class RecordingScheduler : IReminderScheduler
{
    public List<Guid> Scheduled { get; } = [];
    public List<Guid> Unscheduled { get; } = [];

    public Task ScheduleAsync(Guid tenantId, Guid reminderId, DateTime fireAtUtc, CancellationToken ct = default)
    {
        Scheduled.Add(reminderId);
        return Task.CompletedTask;
    }

    public Task RescheduleAsync(Guid tenantId, Guid reminderId, DateTime newFireAtUtc, CancellationToken ct = default)
    {
        Scheduled.Add(reminderId);
        return Task.CompletedTask;
    }

    public Task UnscheduleAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default)
    {
        Unscheduled.Add(reminderId);
        return Task.CompletedTask;
    }

    public Task<bool> IsScheduledAsync(Guid tenantId, Guid reminderId, CancellationToken ct = default) =>
        Task.FromResult(Scheduled.Contains(reminderId));
}

/// <summary>
/// Registra las llamadas al puerto de métricas. Se guardan los tags como string porque lo que
/// interesa probar es <b>qué</b> se etiquetó (que la razón de cancelación llegue normalizada, que el
/// misfire diga por qué política), no el instrumento OTel — ese lo cubre
/// <c>ReminderMetricsTests</c> contra el <c>Meter</c> real.
/// </summary>
internal sealed class RecordingReminderMetrics : IReminderMetrics
{
    public List<ReminderCategory> Scheduled { get; } = [];
    public List<ReminderCategory> Fired { get; } = [];
    public List<double> FireDelaysSeconds { get; } = [];
    public List<string> Cancelled { get; } = [];
    public List<string> Misfired { get; } = [];
    public List<string> DuplicatesSuppressed { get; } = [];

    public void RecordScheduled(ReminderCategory category) => Scheduled.Add(category);

    public void RecordFired(ReminderCategory category) => Fired.Add(category);

    public void RecordFireDelaySeconds(double seconds) => FireDelaysSeconds.Add(seconds);

    public void RecordCancelled(string reason) => Cancelled.Add(reason);

    public void RecordMisfired(string policy) => Misfired.Add(policy);

    public void RecordDuplicateSuppressed(string resolution) => DuplicatesSuppressed.Add(resolution);
}

internal sealed class NoOpUnitOfWork : IUnitOfWork
{
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
}

/// <summary>Captura lo publicado; el resto de <see cref="IMessageBus"/> no se usa y lanza si se llama.</summary>
internal sealed class RecordingMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public string? TenantId
    {
        get => null;
        set { }
    }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();
}

/// <summary><see cref="ICorrelationContext"/> de test: correlación fija y <c>Push</c> que no hace nada.</summary>
internal sealed class FixedCorrelationContext(string correlationId = "test-correlation") : ICorrelationContext
{
    public string CorrelationId { get; private set; } = correlationId;

    public void Set(string value) => CorrelationId = value;

    public IDisposable Push(string value) => new Scope(this, value);

    private sealed class Scope : IDisposable
    {
        private readonly FixedCorrelationContext _context;
        private readonly string _previous;

        internal Scope(FixedCorrelationContext context, string value)
        {
            _context = context;
            _previous = context.CorrelationId;
            context.CorrelationId = value;
        }

        public void Dispose() => _context.CorrelationId = _previous;
    }
}
