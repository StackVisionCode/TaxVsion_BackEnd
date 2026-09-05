using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Application.Generations.OnboardingReceipt;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;
using Xunit;

namespace TaxVision.Documents.Tests.Unit;

public sealed class GenerateOnboardingReceiptDocumentHandlerTests
{
    [Fact]
    public async Task Requeues_failed_generation_for_same_idempotency_key()
    {
        var onboardingId = Guid.NewGuid();
        var idempotencyKey = $"onb-receipt:{onboardingId:N}";
        var existing = NewGeneration(onboardingId, idempotencyKey);
        Assert.True(
            existing.Fail("Documents.Pdf.ConversionFailed", "HTML to PDF conversion failed.", DateTime.UtcNow).IsSuccess
        );

        var repository = new InMemoryDocumentGenerationRepository(existing);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        var result = await GenerateOnboardingReceiptDocumentHandler.Handle(
            NewCommand(onboardingId, idempotencyKey),
            repository,
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<GenerateOnboardingReceiptDocumentResult>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.GenerationId);
        Assert.Equal(DocumentGenerationStatus.Queued.ToString(), result.Value.Status);
        Assert.Equal(DocumentGenerationStatus.Queued, existing.Status);
        Assert.Null(existing.ErrorCode);
        Assert.Null(existing.ErrorMessage);
        Assert.Equal(1, unitOfWork.SaveCount);

        var process = Assert.Single(bus.Published.OfType<ProcessOnboardingReceiptGenerationCommand>());
        Assert.Equal(existing.Id, process.GenerationId);
        Assert.Equal(onboardingId, process.OnboardingId);
        Assert.Equal("corr-1", process.CorrelationId);
    }

    [Fact]
    public async Task Existing_non_failed_generation_is_not_requeued()
    {
        var onboardingId = Guid.NewGuid();
        var idempotencyKey = $"onb-receipt:{onboardingId:N}";
        var existing = NewGeneration(onboardingId, idempotencyKey);
        var repository = new InMemoryDocumentGenerationRepository(existing);
        var unitOfWork = new RecordingUnitOfWork();
        var bus = new RecordingMessageBus();

        var result = await GenerateOnboardingReceiptDocumentHandler.Handle(
            NewCommand(onboardingId, idempotencyKey),
            repository,
            unitOfWork,
            bus,
            new TestCorrelationContext("corr-1"),
            TimeProvider.System,
            NullLogger<GenerateOnboardingReceiptDocumentResult>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.GenerationId);
        Assert.Equal(DocumentGenerationStatus.Requested.ToString(), result.Value.Status);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCount);
    }

    private static DocumentGeneration NewGeneration(Guid onboardingId, string idempotencyKey)
    {
        var result = DocumentGeneration.Request(
            tenantId: PlatformTenant.Id,
            documentType: DocumentType.Create("OnboardingReceipt").Value,
            templateKey: TemplateKey.Create("onboarding.receipt.v1").Value,
            templateVersion: 1,
            outputFormat: DocumentOutputFormat.Pdf,
            owner: new GenerationOwner("Onboarding", onboardingId),
            sourceService: "auth",
            documentVersion: 1,
            priority: DocumentPriority.High,
            idempotencyKey: idempotencyKey,
            correlationId: "corr-1",
            causationId: null,
            nowUtc: DateTime.UtcNow
        );

        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private static GenerateOnboardingReceiptDocumentCommand NewCommand(Guid onboardingId, string idempotencyKey) =>
        new(
            OnboardingId: onboardingId,
            DocumentVersion: 1,
            TemplateKey: "onboarding.receipt.v1",
            TemplateVersion: 1,
            SourceService: "auth",
            IdempotencyKey: idempotencyKey,
            CorrelationId: "corr-1",
            Receipt: new OnboardingReceiptPayload(
                "Ada",
                "Lovelace",
                "ada@example.com",
                "Growth",
                4900,
                "USD",
                DateTime.UtcNow,
                "4242",
                "Visa **** 4242"
            )
        );

    private sealed class InMemoryDocumentGenerationRepository(params DocumentGeneration[] generations)
        : IDocumentGenerationRepository
    {
        private readonly List<DocumentGeneration> _generations = [.. generations];

        public Task<DocumentGeneration?> GetByIdAsync(
            Guid tenantId,
            Guid generationId,
            CancellationToken ct = default
        ) => Task.FromResult(_generations.FirstOrDefault(g => g.TenantId == tenantId && g.Id == generationId));

        public Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string idempotencyKey,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _generations.FirstOrDefault(g => g.TenantId == tenantId && g.IdempotencyKey == idempotencyKey)
            );

        public Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            Task.FromResult(_generations.FirstOrDefault(g => g.FileId == fileId));

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
