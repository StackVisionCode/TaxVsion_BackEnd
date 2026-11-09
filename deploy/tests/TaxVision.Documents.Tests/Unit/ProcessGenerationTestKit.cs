using System.Text.Encodings.Web;
using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Documents.Application.Abstractions;
using TaxVision.Documents.Domain.Branding;
using TaxVision.Documents.Domain.Generations;
using TaxVision.Documents.Domain.ValueObjects;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.Documents.Tests.Unit;

/// <summary>Dobles de los handlers de proceso. <see cref="Journal"/> guarda el orden de guardados y publicaciones.</summary>
internal static class ProcessGenerationTestKit
{
    private static readonly JsonSerializerOptions SnapshotOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = true,
    };

    // Snapshot legible del diccionario de render, en el orden de sus claves.
    public static string Snapshot(IReadOnlyDictionary<string, object?> data) =>
        JsonSerializer.Serialize<object>(data, SnapshotOptions).ReplaceLineEndings("\n");

    public static DocumentGeneration NewRequested(
        Guid tenantId,
        string documentType,
        string templateKey,
        string ownerType
    ) =>
        DocumentGeneration
            .Request(
                tenantId: tenantId,
                documentType: DocumentType.Create(documentType).Value,
                templateKey: TemplateKey.Create(templateKey).Value,
                templateVersion: 1,
                outputFormat: DocumentOutputFormat.Pdf,
                owner: new GenerationOwner(ownerType, Guid.NewGuid()),
                sourceService: "tasks",
                documentVersion: 1,
                priority: DocumentPriority.Normal,
                idempotencyKey: Guid.NewGuid().ToString("N"),
                correlationId: "corr",
                causationId: null,
                nowUtc: new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc)
            )
            .Value;

    internal sealed class Journal
    {
        public List<string> Entries { get; } = [];
    }

    internal sealed class Generations(DocumentGeneration? generation) : IDocumentGenerationRepository
    {
        public Task<DocumentGeneration?> GetByIdAsync(
            Guid tenantId,
            Guid generationId,
            CancellationToken ct = default
        ) => Task.FromResult(generation);

        public Task<DocumentGeneration?> GetByIdempotencyKeyAsync(
            Guid tenantId,
            string sourceService,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new NotSupportedException();

        public Task<DocumentGeneration?> GetByFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<DocumentGeneration?> GetBySignatureCopyFileIdAsync(Guid fileId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(DocumentGeneration value, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    internal sealed class Branding(DocumentBranding? stored = null) : IDocumentBrandingRepository
    {
        public Task<DocumentBranding?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(stored);

        public Task AddAsync(DocumentBranding branding, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    // Logo por tenant; un tenant en Failing lanza, como la proyección ausente o el storage caído.
    internal sealed class Logos : ITenantLogoResolver
    {
        public Dictionary<Guid, string> ByTenant { get; } = [];
        public HashSet<Guid> Failing { get; } = [];
        public List<(Guid Tenant, LogoAudience Audience)> Calls { get; } = [];

        public Task<string?> ResolveLogoDataUriAsync(
            Guid tenantId,
            LogoAudience audience,
            CancellationToken ct = default
        )
        {
            Calls.Add((tenantId, audience));
            if (Failing.Contains(tenantId))
                throw new InvalidOperationException("Logo unavailable.");
            return Task.FromResult<string?>(ByTenant.GetValueOrDefault(tenantId));
        }
    }

    internal sealed class Renderer(Error? failure = null) : IDocumentTemplateRenderer
    {
        public string? TemplateKey { get; private set; }
        public int TemplateVersion { get; private set; }
        public Guid TenantId { get; private set; }
        public IReadOnlyDictionary<string, object?>? Data { get; private set; }

        public Task<Result<string>> RenderHtmlAsync(
            string templateKey,
            int templateVersion,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> data,
            CancellationToken ct = default
        )
        {
            TemplateKey = templateKey;
            TemplateVersion = templateVersion;
            TenantId = tenantId;
            Data = data;
            return Task.FromResult(failure is null ? Result.Success("<html />") : Result.Failure<string>(failure));
        }
    }

    internal sealed class Pdf(Error? failure = null) : IHtmlToPdfConverter
    {
        public Task<Result<byte[]>> ConvertAsync(string html, CancellationToken ct = default) =>
            Task.FromResult(
                failure is null
                    ? Result.Success(new byte[] { 0x25, 0x50, 0x44, 0x46 })
                    : Result.Failure<byte[]>(failure)
            );
    }

    // Acepta todo salvo la subida número FailOnUpload (1-indexada).
    internal sealed class Storage : IDocumentStorageClient
    {
        public int? FailOnUpload { get; init; }
        public List<Guid> Uploads { get; } = [];

        public Task<Result<StorageUploadResult>> UploadAsync(
            Guid fileId,
            byte[] content,
            string fileName,
            string contentType,
            CancellationToken ct = default
        )
        {
            Uploads.Add(fileId);
            return Task.FromResult(
                Uploads.Count == FailOnUpload
                    ? Result.Failure<StorageUploadResult>(new Error("Documents.Storage.Upload", "Refused."))
                    : Result.Success(
                        new StorageUploadResult(
                            fileId,
                            "temp-bucket",
                            $"documents/{fileId:N}/{fileName}",
                            content.LongLength,
                            contentType
                        )
                    )
            );
        }
    }

    internal sealed class Pages(int count, bool throws = false) : IPdfPageCounter
    {
        public int CountPages(byte[] pdf) => throws ? throw new InvalidOperationException("Bad PDF.") : count;
    }

    internal sealed class Qr : IQrCodeGenerator
    {
        public string CreatePngDataUri(string content, int pixelsPerModule = 6) =>
            $"data:image/png;base64,QR({content})";
    }

    internal sealed class UnitOfWork(Journal journal) : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            journal.Entries.Add("save");
            return Task.FromResult(1);
        }
    }

    internal sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Noop();
        }

        private sealed class Noop : IDisposable
        {
            public void Dispose() { }
        }
    }

    internal sealed class Bus(Journal journal) : IMessageBus
    {
        public List<object> Published { get; } = [];

        public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
        {
            journal.Entries.Add($"publish:{typeof(T).Name}");
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
