using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Documents.Application.Generations;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.GenerateInvoiceDocument;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>Reintento con la misma clave: atascada o fallida se reencola; en curso reciente no.</summary>
public sealed class GenerateInvoiceDocumentHandlerTests
{
    private static readonly Guid Tenant = Guid.NewGuid();

    [Fact]
    public async Task Handle_ExistingStalledRequested_RequeuesProcessCommand()
    {
        var invoiceId = Guid.NewGuid();
        var key = $"invoice-pdf:{invoiceId:N}:v1:none";
        var existing = NewGeneration(invoiceId, key, DateTime.UtcNow.AddHours(-2));
        var bus = new RecordingMessageBus();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await HandleAsync(NewCommand(invoiceId, key), new InMemoryRepository(existing), unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.GenerationId);
        Assert.Equal(DocumentGenerationStatus.Queued.ToString(), result.Value.Status);
        Assert.Equal(DocumentGenerationStatus.Queued, existing.Status);
        Assert.Equal(1, unitOfWork.SaveCount);
        var process = Assert.Single(bus.Published.OfType<ProcessInvoiceGenerationCommand>());
        Assert.Equal(existing.Id, process.GenerationId);
        Assert.Equal(invoiceId, process.OwnerId);
    }

    [Fact]
    public async Task Handle_ExistingFailed_RetriesFromFailure()
    {
        var invoiceId = Guid.NewGuid();
        var key = $"invoice-pdf:{invoiceId:N}:v1:none";
        var existing = NewGeneration(invoiceId, key, DateTime.UtcNow);
        Assert.True(existing.Fail("Documents.Pdf.ConversionFailed", "failed", DateTime.UtcNow).IsSuccess);
        var bus = new RecordingMessageBus();

        var result = await HandleAsync(
            NewCommand(invoiceId, key),
            new InMemoryRepository(existing),
            new RecordingUnitOfWork(),
            bus
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentGenerationStatus.Queued, existing.Status);
        Assert.Null(existing.ErrorCode);
        Assert.Single(bus.Published.OfType<ProcessInvoiceGenerationCommand>());
    }

    [Fact]
    public async Task Handle_ExistingRecentRendering_ReturnsExistingWithoutRequeue()
    {
        var invoiceId = Guid.NewGuid();
        var key = $"invoice-pdf:{invoiceId:N}:v1:none";
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        var existing = NewGeneration(invoiceId, key, oneMinuteAgo);
        Assert.True(existing.Queue(oneMinuteAgo).IsSuccess);
        Assert.True(existing.StartRendering(oneMinuteAgo).IsSuccess);
        var bus = new RecordingMessageBus();
        var unitOfWork = new RecordingUnitOfWork();

        var result = await HandleAsync(NewCommand(invoiceId, key), new InMemoryRepository(existing), unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(DocumentGenerationStatus.Rendering.ToString(), result.Value.Status);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    [Fact]
    public async Task Handle_SameKeyFromOtherSourceService_CreatesNewGeneration()
    {
        var invoiceId = Guid.NewGuid();
        var key = $"shared-key:{invoiceId:N}";
        var fromTasks = NewGeneration(invoiceId, key, DateTime.UtcNow, sourceService: "tasks");
        var repository = new InMemoryRepository(fromTasks);
        var bus = new RecordingMessageBus();

        var result = await HandleAsync(NewCommand(invoiceId, key), repository, new RecordingUnitOfWork(), bus);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(fromTasks.Id, result.Value.GenerationId);
        var process = Assert.Single(bus.Published.OfType<ProcessInvoiceGenerationCommand>());
        Assert.Equal(result.Value.GenerationId, process.GenerationId);
    }

    private static Task<BuildingBlocks.Results.Result<GenerateInvoiceDocumentResult>> HandleAsync(
        GenerateInvoiceDocumentCommand command,
        IDocumentGenerationRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus
    ) =>
        GenerateInvoiceDocumentHandler.Handle(
            command,
            repository,
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            Options.Create(new GenerationRetryOptions()),
            NullLogger<GenerateInvoiceDocumentResult>.Instance,
            CancellationToken.None
        );

    private static DocumentGeneration NewGeneration(
        Guid invoiceId,
        string key,
        DateTime nowUtc,
        string sourceService = "billing"
    ) =>
        DocumentGeneration
            .Request(
                tenantId: Tenant,
                documentType: DocumentType.Create("Invoice").Value,
                templateKey: TemplateKey.Create("billing.invoice.v1").Value,
                templateVersion: 1,
                outputFormat: DocumentOutputFormat.Pdf,
                owner: new GenerationOwner("Invoice", invoiceId),
                sourceService: sourceService,
                documentVersion: 1,
                priority: DocumentPriority.Normal,
                idempotencyKey: key,
                correlationId: "corr-1",
                causationId: null,
                nowUtc: nowUtc
            )
            .Value;

    private static GenerateInvoiceDocumentCommand NewCommand(Guid invoiceId, string key) =>
        new(
            TenantId: Tenant,
            InvoiceId: invoiceId,
            InvoiceNumber: "INV-2026-00042",
            DocumentVersion: 1,
            TemplateKey: "billing.invoice.v1",
            TemplateVersion: 1,
            TaxYear: 2026,
            SourceService: "billing",
            IdempotencyKey: key,
            CorrelationId: "corr-1",
            Invoice: new InvoicePayload(
                "USD",
                new DateOnly(2026, 11, 3),
                null,
                new InvoiceParty("Office", "12-3456789", null),
                new InvoiceParty("Client", "", null),
                [new InvoiceLine("Tax prep", 1, 350m, 350m)],
                350m,
                0m,
                350m,
                null
            )
        );

    private sealed class InMemoryRepository(params DocumentGeneration[] generations) : IDocumentGenerationRepository
    {
        private readonly List<DocumentGeneration> _generations = [.. generations];

        public Task<DocumentGeneration?> GetByIdAsync(
            Guid tenantId,
            Guid generationId,
            CancellationToken ct = default
        ) => Task.FromResult(_generations.FirstOrDefault(g => g.TenantId == tenantId && g.Id == generationId));

        public Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string sourceService,
            string idempotencyKey,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _generations.FirstOrDefault(g =>
                    g.TenantId == tenantId && g.SourceService == sourceService && g.IdempotencyKey == idempotencyKey
                )
            );

        public Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_generations.FirstOrDefault(g => g.FileId == fileId));

        public Task<DocumentGeneration?> GetBySignatureCopyFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_generations.FirstOrDefault(g => g.SignatureCopyFileId == fileId));

        public Task AddAsync(DocumentGeneration generation, CancellationToken ct = default)
        {
            _generations.Add(generation);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(1);
        }
    }

    private sealed class TestCorrelationContext(string correlationId) : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = correlationId;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            var previous = CorrelationId;
            CorrelationId = correlationId;
            return new Popper(() => CorrelationId = previous);
        }

        private sealed class Popper(Action pop) : IDisposable
        {
            public void Dispose() => pop();
        }
    }

    private sealed class RecordingMessageBus : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            if (message is not null)
                Published.Add(message);
            return ValueTask.CompletedTask;
        }

        public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) =>
            throw new NotImplementedException();

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

        public string? TenantId { get; set; }

        public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
            throw new NotImplementedException();

        public Task InvokeAsync(
            object message,
            DeliveryOptions options,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

        public Task<T> InvokeAsync<T>(
            object message,
            CancellationToken cancellation = default,
            TimeSpan? timeout = null
        ) => throw new NotImplementedException();

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
}
