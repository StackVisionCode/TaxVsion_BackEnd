using System.Collections.Concurrent;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Accounts;
using TaxVision.Connectors.MailboxListener.Imap;
using TaxVision.Connectors.MailboxListener.Sessions;

namespace TaxVision.Connectors.Tests.MailboxListener;

/// <summary>Reloj que se mueve a mano; las esperas (debounce, backoff) terminan al instante.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
    {
        if (dueTime != Timeout.InfiniteTimeSpan)
            ThreadPool.QueueUserWorkItem(_ => callback(state));
        return new NoOpTimer();
    }

    private sealed class NoOpTimer : ITimer
    {
        public bool Change(TimeSpan dueTime, TimeSpan period) => true;

        public void Dispose() { }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Buzón guionado: cada espera devuelve el siguiente resultado; sin guion, espera hasta la cancelación.</summary>
internal sealed class ScriptedMailboxWatcher : IMailboxWatcher
{
    public Exception? ConnectFailure { get; init; }
    public bool SupportsIdle { get; init; } = true;
    public Queue<bool> Waits { get; } = new();
    public MailboxConnectionInfo? ConnectedWith { get; private set; }
    public bool Disposed { get; private set; }

    public Task ConnectAsync(MailboxConnectionInfo connection, CancellationToken ct)
    {
        if (ConnectFailure is not null)
            throw ConnectFailure;
        ConnectedWith = connection;
        return Task.CompletedTask;
    }

    public async Task<bool> WaitForChangeAsync(TimeSpan maxWait, CancellationToken ct)
    {
        if (Waits.TryDequeue(out var changed))
            return changed;
        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
        return false;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class ScriptedWatcherFactory(params ScriptedMailboxWatcher[] watchers) : IMailboxWatcherFactory
{
    private readonly Queue<ScriptedMailboxWatcher> _pending = new(watchers);
    public List<ScriptedMailboxWatcher> Created { get; } = [];

    public IMailboxWatcher Create()
    {
        var watcher = _pending.Count > 0 ? _pending.Dequeue() : new ScriptedMailboxWatcher();
        Created.Add(watcher);
        return watcher;
    }
}

internal sealed class FixedConnectionSource(MailboxConnectionInfo? connection) : IMailboxConnectionSource
{
    public int Loads { get; private set; }

    public Task<MailboxConnectionInfo?> LoadAsync(Guid accountId, CancellationToken ct)
    {
        Loads++;
        return Task.FromResult(connection);
    }
}

internal sealed class RecordingChangePublisher : IMailboxChangePublisher
{
    private readonly ConcurrentQueue<Guid> _changes = new();
    private readonly ConcurrentQueue<Guid> _authFailures = new();

    public IReadOnlyCollection<Guid> Changes => _changes;
    public IReadOnlyCollection<Guid> AuthFailures => _authFailures;

    public Task PublishChangeAsync(ActiveAccountRef account, CancellationToken ct)
    {
        _changes.Enqueue(account.Id);
        return Task.CompletedTask;
    }

    public Task PublishAuthFailedAsync(ActiveAccountRef account, CancellationToken ct)
    {
        _authFailures.Enqueue(account.Id);
        return Task.CompletedTask;
    }

    public async Task WaitForChangesAsync(int count)
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (_changes.Count < count)
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Expected {count} change notices, got {_changes.Count}.");
            await Task.Delay(10);
        }
    }
}

internal sealed class InMemoryMailboxListenerPresence : IMailboxListenerPresence
{
    private readonly ConcurrentDictionary<Guid, byte> _listening = new();

    public bool FailOnMark { get; init; }
    public bool SafetyNetSlotFree { get; set; }
    public int Marks { get; private set; }

    public void Listen(Guid accountId) => _listening[accountId] = 0;

    public Task MarkListeningAsync(Guid accountId, TimeSpan ttl, CancellationToken ct = default)
    {
        Marks++;
        if (FailOnMark)
            throw new InvalidOperationException("redis down");
        _listening[accountId] = 0;
        return Task.CompletedTask;
    }

    public Task<bool> IsListeningAsync(Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_listening.ContainsKey(accountId));

    public Task<bool> TryClaimSafetyNetSlotAsync(Guid accountId, TimeSpan interval, CancellationToken ct = default) =>
        Task.FromResult(SafetyNetSlotFree);
}

/// <summary>Sesión de mentira para el coordinador: corre hasta que la cancelen o hasta <see cref="End"/>.</summary>
internal sealed class ControllableSession : IMailboxSession
{
    private readonly TaskCompletionSource _ended = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool WasCancelled { get; private set; }

    public void End() => _ended.TrySetResult();

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            await _ended.Task.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            WasCancelled = true;
        }
    }
}

internal sealed class ControllableSessionFactory : IMailboxSessionFactory
{
    public Dictionary<Guid, ControllableSession> Sessions { get; } = [];
    public int Created { get; private set; }

    public IMailboxSession Create(ActiveAccountRef account)
    {
        Created++;
        var session = new ControllableSession();
        Sessions[account.Id] = session;
        return session;
    }
}
