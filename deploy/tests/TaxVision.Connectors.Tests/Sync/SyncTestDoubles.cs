using BuildingBlocks.Results;
using TaxVision.Connectors.Application.Providers;
using TaxVision.Connectors.Application.Sync;
using TaxVision.Connectors.Domain.Shared;
using TaxVision.Connectors.Domain.Sync;

namespace TaxVision.Connectors.Tests.Sync;

internal sealed class FakeProviderSyncCursorRepository : IProviderSyncCursorRepository
{
    public List<ProviderSyncCursor> Cursors { get; } = [];

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
    public List<string?> ReceivedCursors { get; } = [];

    public Task<HistoryPage> GetHistoryAsync(Guid accountId, string? sinceCursor, CancellationToken ct = default)
    {
        ReceivedCursors.Add(sinceCursor);
        if (ThrowOnGetHistory is not null)
            throw ThrowOnGetHistory;

        return Task.FromResult(OnGetHistory?.Invoke(accountId, sinceCursor) ?? new HistoryPage([], sinceCursor, false));
    }

    public Task<RawMessage> GetMessageAsync(Guid accountId, string providerMessageId, CancellationToken ct = default)
    {
        if (ThrowOnGetMessageFor.Contains(providerMessageId))
            throw new EmailProviderException($"Could not fetch {providerMessageId}.");

        return Task.FromResult(
            OnGetMessage?.Invoke(accountId, providerMessageId)
                ?? new RawMessage(
                    providerMessageId,
                    null,
                    null,
                    null,
                    [],
                    "customer@example.com",
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

    public Func<Guid, string, string, byte[]>? OnGetAttachment { get; set; }
    public Exception? ThrowOnGetAttachment { get; set; }

    public Task<Stream> GetAttachmentAsync(
        Guid accountId,
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

internal sealed class FakeEmailProviderClientFactory(IEmailProviderClient client) : IEmailProviderClientFactory
{
    public Result<IEmailProviderClient> Resolve(ProviderCode providerCode) => Result.Success(client);
}
