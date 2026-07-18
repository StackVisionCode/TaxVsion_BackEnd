using System.Linq;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Common;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Application.RateLimit;
using TaxVision.Postmaster.Application.Sending;
using TaxVision.Postmaster.Application.Suppression;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Domain.Suppression;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Postmaster.Tests.Consumers;

/// <summary>Fake mínimo de IMessageBus — solo captura lo publicado vía PublishAsync.</summary>
internal sealed class FakeMessageBus : IMessageBus
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

internal sealed class FakeUnitOfWork : IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    /// <summary>
    /// Simula el backstop del índice único de <c>SentMessages</c> (plan §Fase 11, punto 4): cuando se
    /// setea, el N-ésimo <see cref="SaveChangesAsync"/> (1-indexado) lanza <see cref="ConflictException"/>
    /// en vez de completar — reproduce, sin necesitar SQL Server real, el momento exacto en que el
    /// índice único de EF revienta en el perdedor de una carrera.
    /// </summary>
    public int? ThrowConflictOnSaveChangesCall { get; set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        if (ThrowConflictOnSaveChangesCall == SaveChangesCallCount)
            throw new ConflictException(
                "Persistence.UniqueConstraint",
                "A record with the same unique values already exists."
            );
        return Task.FromResult(0);
    }
}

internal sealed class FakeCorrelationContext : ICorrelationContext
{
    public string CorrelationId { get; private set; } = string.Empty;

    public void Set(string correlationId) => CorrelationId = correlationId;

    public IDisposable Push(string correlationId)
    {
        var previous = CorrelationId;
        CorrelationId = correlationId;
        return new Popper(this, previous);
    }

    private sealed class Popper(FakeCorrelationContext owner, string previous) : IDisposable
    {
        public void Dispose() => owner.CorrelationId = previous;
    }
}

internal sealed class FakeIdempotencyGuard : IIdempotencyGuard
{
    public IdempotencyReservationResult ReserveReturnValue { get; set; } = IdempotencyReservationResult.Reserved();
    public List<(Guid TenantId, string Key, Guid SentMessageId)> Completed { get; } = [];

    public Task<IdempotencyReservationResult> TryReserveAsync(
        Guid tenantId,
        string idempotencyKey,
        CancellationToken ct
    ) => Task.FromResult(ReserveReturnValue);

    public Task CompleteAsync(Guid tenantId, string idempotencyKey, Guid sentMessageId, CancellationToken ct)
    {
        Completed.Add((tenantId, idempotencyKey, sentMessageId));
        return Task.CompletedTask;
    }
}

internal sealed class FakeProviderResolver : IProviderResolver
{
    public ResolveResult ResolveReturnValue { get; set; } =
        new(ProviderResolutionStatus.SystemProviderMissing, null, "not configured");

    public Task<ResolveResult> ResolveAsync(
        Guid tenantId,
        TaxVision.Postmaster.Domain.Providers.ProviderScope requiredScope,
        ProviderPriorityHint? priorityHint,
        CancellationToken ct
    ) => Task.FromResult(ResolveReturnValue);
}

internal sealed class FakeEmailSender : IEmailSender
{
    public SendResult SendReturnValue { get; set; } = new(true, "provider-msg-1", null, []);
    public SentMessage? LastMessage { get; private set; }

    /// <summary>Hardening Fase 9 — captura lo que el consumer realmente pasó, para probar que las
    /// referencias del evento efectivamente llegan como bytes hasta acá.</summary>
    public IReadOnlyList<InlineAssetBytes>? LastInlineAssets { get; private set; }

    public Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedEmailProvider provider,
        IReadOnlyList<InlineAssetBytes> inlineAssets,
        CancellationToken ct
    )
    {
        LastMessage = message;
        LastInlineAssets = inlineAssets;
        return Task.FromResult(SendReturnValue);
    }
}

internal sealed class FakeSentMessageRepository : ISentMessageRepository
{
    public List<SentMessage> Added { get; } = [];

    public Task AddAsync(SentMessage message, CancellationToken ct = default)
    {
        Added.Add(message);
        return Task.CompletedTask;
    }

    public Task<Result<SentMessage>> GetByIdWithEventsAsync(Guid tenantId, Guid id, CancellationToken ct = default)
    {
        var message = Added.Find(m => m.Id == id && m.TenantId == tenantId);
        return Task.FromResult(
            message is null
                ? Result.Failure<SentMessage>(new Error("SentMessage.NotFound", $"SentMessage {id} not found."))
                : Result.Success(message)
        );
    }
}

internal sealed class FakeEmailProviderRateLimiter : IEmailProviderRateLimiter
{
    public RateLimitDecision DecisionReturnValue { get; set; } = new(true, null);

    public Task<RateLimitDecision> AcquireAsync(
        string providerCode,
        Guid tenantId,
        int limitPerMinute,
        CancellationToken ct = default
    ) => Task.FromResult(DecisionReturnValue);
}

internal sealed class FakeOAuthProviderResolver : IOAuthProviderResolver
{
    public OAuthResolveResult ResolveReturnValue { get; set; } =
        new(OAuthResolutionStatus.ProviderNotConfigured, null, "not configured");

    public Task<OAuthResolveResult> ResolveAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(ResolveReturnValue);

    public Task<OAuthResolveResult> ResolveByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct) =>
        Task.FromResult(ResolveReturnValue);
}

internal sealed class FakeOAuthEmailSender : IOAuthEmailSender
{
    public SendResult SendReturnValue { get; set; } = new(true, "connectors-msg-1", null, []);
    public SentMessage? LastMessage { get; private set; }

    public Task<SendResult> SendAsync(
        SentMessage message,
        RenderedContent content,
        ResolvedOAuthProvider provider,
        string? inReplyToInternetMessageId,
        IReadOnlyList<string>? references,
        string? replyToProviderMessageId,
        IReadOnlyList<OutboundAttachmentBytes> attachments,
        CancellationToken ct
    )
    {
        LastMessage = message;
        return Task.FromResult(SendReturnValue);
    }
}

internal sealed class FakeOutboundAttachmentFetcher : IOutboundAttachmentFetcher
{
    public Result<IReadOnlyList<OutboundAttachmentBytes>> FetchReturnValue { get; set; } =
        Result.Success<IReadOnlyList<OutboundAttachmentBytes>>([]);

    public Task<Result<IReadOnlyList<OutboundAttachmentBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<OutboundAttachmentRef> attachments,
        CancellationToken ct
    ) => Task.FromResult(FetchReturnValue);
}

/// <summary>
/// Fake de <see cref="IInlineAssetFetcher"/> (Hardening Fase 9) — por default devuelve un
/// <see cref="InlineAssetBytes"/> "sintético" por cada <see cref="InlineAsset"/> recibido (bytes
/// determinísticos derivados del ContentId), así que un test puede probar que lo que entra
/// (referencias del evento) efectivamente sale (bytes en <c>IEmailSender.SendAsync</c>) sin acoplarse
/// a la implementación real de CloudStorage. <see cref="FetchReturnValue"/> permite simular una falla
/// de fetch (degradación con gracia, ver <c>FetchInlineAssetBytesAsync</c> del consumer).
/// </summary>
internal sealed class FakeInlineAssetFetcher : IInlineAssetFetcher
{
    public Result<IReadOnlyList<InlineAssetBytes>>? FetchReturnValue { get; set; }
    public IReadOnlyList<InlineAsset>? LastRequested { get; private set; }

    public Task<Result<IReadOnlyList<InlineAssetBytes>>> FetchAllAsync(
        Guid tenantId,
        IReadOnlyList<InlineAsset> inlineAssets,
        CancellationToken ct
    )
    {
        LastRequested = inlineAssets;
        if (FetchReturnValue is not null)
            return Task.FromResult(FetchReturnValue);

        var bytes = inlineAssets
            .Select(a => new InlineAssetBytes(
                a.ContentId,
                Encoding.UTF8.GetBytes($"fake-bytes-{a.ContentId}"),
                a.ContentType,
                $"{a.ContentId}.png"
            ))
            .ToList();
        return Task.FromResult(Result.Success<IReadOnlyList<InlineAssetBytes>>(bytes));
    }
}

internal sealed class FakeSuppressionListRepository : ISuppressionListRepository
{
    /// <summary>Seed rápido para tests de suppression-check pre-send (Fase 7) — no pasa por AddAsync.</summary>
    public HashSet<string> SuppressedAddresses { get; } = [];

    public List<SuppressionListEntry> Entries { get; } = [];

    public Task AddAsync(SuppressionListEntry entry, CancellationToken ct = default)
    {
        Entries.Add(entry);
        return Task.CompletedTask;
    }

    public Task<Result<SuppressionListEntry>> GetByAddressAsync(
        Guid tenantId,
        string emailAddress,
        CancellationToken ct = default
    )
    {
        var normalized = SuppressionListEntry.NormalizeAddress(emailAddress);
        var entry = Entries.Find(e => e.TenantId == tenantId && e.EmailAddress == normalized);
        return Task.FromResult(
            entry is null
                ? Result.Failure<SuppressionListEntry>(
                    new Error("SuppressionListEntry.NotFound", $"'{emailAddress}' is not suppressed.")
                )
                : Result.Success(entry)
        );
    }

    public Task<IReadOnlyList<SuppressionListEntry>> ListAsync(
        Guid tenantId,
        string? addressFilter,
        SuppressionReason? reasonFilter,
        int page,
        int pageSize,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlySet<string>> GetSuppressedAsync(
        Guid tenantId,
        IReadOnlyCollection<string> normalizedAddresses,
        CancellationToken ct = default
    ) => Task.FromResult<IReadOnlySet<string>>(normalizedAddresses.Where(SuppressedAddresses.Contains).ToHashSet());

    public Task<bool> RemoveAsync(Guid tenantId, string emailAddress, CancellationToken ct = default) =>
        throw new NotImplementedException();
}
