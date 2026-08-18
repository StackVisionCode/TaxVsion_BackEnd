using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Notes;
using TaxVision.Notes.Domain.Projections;
using Wolverine;
using Wolverine.Transports;

namespace TaxVision.Notes.Tests.Application;

/// <summary>
/// Fakes de mano compartidos por los tests de handlers de Fase 5 (Commands/Queries) — mismo
/// criterio que <c>PermissionsProjectionConsumersTests.cs</c>: sin Moq, solo lo mínimo que cada
/// handler realmente usa (el resto lanza <see cref="NotImplementedException"/> si algún handler
/// nuevo llegara a necesitarlo, para que el test falle explícito en vez de silencioso).
/// </summary>
internal sealed class FakeNoteRepository : INoteRepository
{
    private readonly Dictionary<Guid, Note> _byId = [];

    public void Seed(Note note) => _byId[note.Id] = note;

    public Task<Note?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        Task.FromResult(_byId.TryGetValue(id, out var note) && note.TenantId == tenantId ? note : null);

    public Task<Note?> GetByAttachmentFileIdAsync(Guid cloudStorageFileId, CancellationToken ct = default) =>
        Task.FromResult(
            _byId.Values.FirstOrDefault(n => n.Attachments.Any(a => a.CloudStorageFileId == cloudStorageFileId))
        );

    public Task<PagedResult<Note>> ListByReferenceAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var items = _byId
            .Values.Where(n =>
                n.TenantId == tenantId
                && n.Reference.TargetType == targetType
                && n.Reference.TargetId == targetId
                && n.Status != NoteStatus.Deleted
                && (n.Visibility != NoteVisibility.Private || n.CreatedByUserId == actorUserId || actorHasViewAll)
            )
            .ToList();
        return Task.FromResult(new PagedResult<Note>(items, page, size, items.Count));
    }

    public Task<PagedResult<Note>> ListForAuthorAsync(
        Guid tenantId,
        Guid authorUserId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var items = _byId
            .Values.Where(n =>
                n.TenantId == tenantId && n.CreatedByUserId == authorUserId && n.Status != NoteStatus.Deleted
            )
            .ToList();
        return Task.FromResult(new PagedResult<Note>(items, page, size, items.Count));
    }

    public Task<PagedResult<Note>> SearchAsync(
        Guid tenantId,
        string term,
        Guid actorUserId,
        bool actorHasViewAll,
        int page,
        int size,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<PagedResult<Note>> ListClientVisibleAsync(
        Guid tenantId,
        NoteTargetType targetType,
        Guid targetId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var items = _byId
            .Values.Where(n =>
                n.TenantId == tenantId
                && n.Reference.TargetType == targetType
                && n.Reference.TargetId == targetId
                && n.Status != NoteStatus.Deleted
                && n.Visibility == NoteVisibility.ClientVisible
            )
            .ToList();
        return Task.FromResult(new PagedResult<Note>(items, page, size, items.Count));
    }

    public Task AddAsync(Note note, CancellationToken ct = default)
    {
        _byId[note.Id] = note;
        return Task.CompletedTask;
    }
}

internal sealed class FakeCustomerDirectoryRepository(bool exists = true) : ICustomerDirectoryRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult(exists);

    public Task<string?> GetDisplayNameAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task<CustomerDirectoryEntry?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task AddAsync(CustomerDirectoryEntry entry, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task UpsertBulkAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> customerIds,
        DateTime observedAtUtc,
        CancellationToken ct = default
    ) => throw new NotImplementedException();

    public Task<IReadOnlyList<Guid>> ListTenantIdsWithMissingNamesAsync(int limit, CancellationToken ct = default) =>
        throw new NotImplementedException();

    public Task ApplyDisplayNameIfMissingAsync(
        Guid tenantId,
        Guid customerId,
        string displayName,
        CancellationToken ct = default
    ) => throw new NotImplementedException();
}

internal sealed class PassThroughHtmlSanitizer : IHtmlSanitizer
{
    public string Sanitize(string rawHtml) => rawHtml;
}

internal sealed class NoOpUnitOfWork : IUnitOfWork
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

/// <summary>Fake mínimo de <c>IMessageBus</c> — solo captura lo publicado vía PublishAsync; el resto lanza si algún handler nuevo llegara a usarlo.</summary>
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
