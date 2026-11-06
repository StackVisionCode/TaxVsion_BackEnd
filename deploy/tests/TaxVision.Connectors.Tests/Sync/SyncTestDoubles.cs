using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Abstractions;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Tests.Sync;

internal sealed class FakeProviderSyncCursorRepository : IProviderSyncCursorRepository
{
    public List<ProviderSyncCursor> Cursors { get; } = [];

    public void Remove(ProviderSyncCursor cursor) => Cursors.Remove(cursor);

    public Task AddAsync(ProviderSyncCursor cursor, CancellationToken ct = default)
    {
        Cursors.Add(cursor);
        return Task.CompletedTask;
    }

    public Task<Result<ProviderSyncCursor>> GetByAccountIdAsync(Guid accountId, CancellationToken ct = default)
    {
        var cursor = Cursors.Find(c => c.AccountId == accountId);
        return Task.FromResult(
            cursor is null
                ? Result.Failure<ProviderSyncCursor>(new Error("ProviderSyncCursor.NotFound", "Not found."))
                : Result.Success(cursor)
        );
    }
}

internal sealed class FakeEmailProviderClient(ProviderCode providerCode) : IEmailProviderClient
{
    public ProviderCode ProviderCode { get; } = providerCode;
    public Func<Guid, string?, HistoryPage>? OnGetHistory { get; set; }
    public Func<Guid, string, RawMessage>? OnGetMessage { get; set; }
    public Exception? ThrowOnGetHistory { get; set; }
    public HashSet<string> ThrowOnGetMessageFor { get; } = [];

    /// <summary>El proveedor responde que el mensaje ya no existe (borrado entre history y fetch).</summary>
    public HashSet<string> NotFoundOnGetMessageFor { get; } = [];
    public List<string?> ReceivedCursors { get; } = [];

    /// <summary>Falla el lote entero (ej. se cortó la conexión IMAP a mitad del FETCH).</summary>
    public Exception? ThrowOnGetMessages { get; set; }

    public async Task<IReadOnlyList<ProviderMessageFetch>> GetMessagesAsync(
        Guid accountId,
        Guid tenantId,
        IReadOnlyList<string> providerMessageIds,
        CancellationToken ct = default
    )
    {
        if (ThrowOnGetMessages is not null)
            throw ThrowOnGetMessages;

        var results = new List<ProviderMessageFetch>();
        foreach (var id in providerMessageIds)
        {
            try
            {
                results.Add(ProviderMessageFetch.Found(id, await GetMessageAsync(accountId, tenantId, id, ct)));
            }
            catch (EmailProviderException ex)
            {
                results.Add(ProviderMessageFetch.Failed(id, ex));
            }
        }
        return results;
    }

    public List<DateTime?> ReceivedFirstSyncFrom { get; } = [];

    public Func<DateTime, DateTime, string?, InboxMessageIdPage>? OnListInbox { get; set; }
    public Exception? ThrowOnListInbox { get; set; }
    public List<(DateTime FromUtc, DateTime ToUtc, string? PageToken)> ListInboxCalls { get; } = [];
    public int GetMessageCalls { get; private set; }

    public Task<InboxMessageIdPage> ListInboxMessageIdsAsync(
        Guid accountId,
        Guid tenantId,
        DateTime fromUtc,
        DateTime toUtc,
        string? pageToken,
        CancellationToken ct = default
    )
    {
        ListInboxCalls.Add((fromUtc, toUtc, pageToken));
        if (ThrowOnListInbox is not null)
            throw ThrowOnListInbox;

        return Task.FromResult(OnListInbox?.Invoke(fromUtc, toUtc, pageToken) ?? new InboxMessageIdPage([], null));
    }

    public Task<HistoryPage> GetHistoryAsync(
        Guid accountId,
        Guid tenantId,
        string? sinceCursor,
        DateTime? firstSyncFromUtc = null,
        CancellationToken ct = default
    )
    {
        ReceivedCursors.Add(sinceCursor);
        ReceivedFirstSyncFrom.Add(firstSyncFromUtc);
        if (ThrowOnGetHistory is not null)
            throw ThrowOnGetHistory;

        return Task.FromResult(OnGetHistory?.Invoke(accountId, sinceCursor) ?? new HistoryPage([], sinceCursor, false));
    }

    public Task<RawMessage> GetMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        GetMessageCalls++;
        if (ThrowOnGetMessageFor.Contains(providerMessageId))
            throw new EmailProviderException($"Could not fetch {providerMessageId}.");
        if (NotFoundOnGetMessageFor.Contains(providerMessageId))
            throw new EmailProviderException($"{providerMessageId} was not found.", isNotFound: true);

        return Task.FromResult(
            OnGetMessage?.Invoke(accountId, providerMessageId)
                ?? new RawMessage(
                    providerMessageId,
                    null,
                    null,
                    null,
                    [],
                    "customer@example.com",
                    null,
                    ["office@gmail.com"],
                    [],
                    [],
                    "Subject",
                    "Snippet",
                    DateTime.UtcNow,
                    [],
                    AuthenticationSignals.Unknown
                )
        );
    }

    public Func<Guid, string, MessageBody>? OnGetMessageBody { get; set; }
    public Exception? ThrowOnGetMessageBody { get; set; }

    public Task<MessageBody> GetMessageBodyAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        if (ThrowOnGetMessageBody is not null)
            throw ThrowOnGetMessageBody;

        return Task.FromResult(
            OnGetMessageBody?.Invoke(accountId, providerMessageId)
                ?? new MessageBody(1024, "<p>html</p>", "text", new Dictionary<string, string>(), [])
        );
    }

    public Func<string, byte[]>? OnGetRawMessage { get; set; }
    public Exception? ThrowOnGetRawMessage { get; set; }

    public Task<byte[]> GetRawMessageAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        CancellationToken ct = default
    )
    {
        if (ThrowOnGetRawMessage is not null)
            throw ThrowOnGetRawMessage;
        return Task.FromResult(OnGetRawMessage?.Invoke(providerMessageId) ?? []);
    }

    public Func<Guid, string, string, byte[]>? OnGetAttachment { get; set; }
    public Exception? ThrowOnGetAttachment { get; set; }

    public Task<Stream> GetAttachmentAsync(
        Guid accountId,
        Guid tenantId,
        string providerMessageId,
        string attachmentId,
        CancellationToken ct = default
    )
    {
        if (ThrowOnGetAttachment is not null)
            throw ThrowOnGetAttachment;

        var bytes = OnGetAttachment?.Invoke(accountId, providerMessageId, attachmentId) ?? [1, 2, 3];
        return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
    }
}

internal sealed class FakeProviderMessageFetchFailureRepository : IProviderMessageFetchFailureRepository
{
    public List<ProviderMessageFetchFailure> Failures { get; } = [];

    public Task<IReadOnlyList<ProviderMessageFetchFailure>> ListPendingAsync(
        Guid accountId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<ProviderMessageFetchFailure>>(
            Failures
                .Where(f => f.AccountId == accountId && f.Status == ProviderMessageFetchFailureStatus.Pending)
                .ToList()
        );

    public Task<ProviderMessageFetchFailure?> GetAsync(
        Guid accountId,
        string providerMessageId,
        CancellationToken ct = default
    ) => Task.FromResult(Failures.Find(f => f.AccountId == accountId && f.ProviderMessageId == providerMessageId));

    public Task AddAsync(ProviderMessageFetchFailure failure, CancellationToken ct = default)
    {
        Failures.Add(failure);
        return Task.CompletedTask;
    }
}

internal sealed class FakeSyncMetrics : ISyncMetrics
{
    public int Abandoned { get; private set; }

    public void MessageAbandoned(ProviderCode providerCode) => Abandoned++;
}

internal static class FetchFailures
{
    /// <summary>Tracker con repositorio vacío: para las pruebas que no miran los fallos por mensaje.</summary>
    public static MessageFetchFailureTracker None() =>
        Tracker(new FakeProviderMessageFetchFailureRepository(), new FakeSyncMetrics());

    public static MessageFetchFailureTracker Tracker(
        FakeProviderMessageFetchFailureRepository repository,
        FakeSyncMetrics metrics
    ) =>
        new(
            repository,
            MessageFetchFailureLimits.Default,
            metrics,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<MessageFetchFailureTracker>.Instance
        );
}

internal sealed class FakeEmailProviderClientFactory(IEmailProviderClient client) : IEmailProviderClientFactory
{
    public Result<IEmailProviderClient> Resolve(ProviderCode providerCode) => Result.Success(client);
}

/// <summary>Siempre hay otro aviso pendiente: simula una cuenta muy activa.</summary>
internal sealed class AlwaysPendingSyncFlag : ISyncPendingFlag
{
    public Task MarkAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> TryConsumeAsync(Guid accountId, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>Toma el lock pero al alargarlo ya es de otro (venció a mitad del sync).</summary>
internal sealed class LostOnExtendDistributedLock : IDistributedLock
{
    public Task<ILockHandle> AcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default) =>
        Task.FromResult<ILockHandle>(new Handle(key));

    private sealed class Handle(string key) : ILockHandle
    {
        public bool IsAcquired => true;
        public string Key => key;

        public Task<bool> ExtendAsync(TimeSpan ttl, CancellationToken ct = default) => Task.FromResult(false);

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

/// <summary>Llegó un solo aviso mientras se sincronizaba.</summary>
internal sealed class MarkOnceSyncFlag : ISyncPendingFlag
{
    private bool _pending = true;

    public Task MarkAsync(Guid accountId, CancellationToken ct = default) => Task.CompletedTask;

    public Task<bool> TryConsumeAsync(Guid accountId, CancellationToken ct = default)
    {
        var wasPending = _pending;
        _pending = false;
        return Task.FromResult(wasPending);
    }
}
