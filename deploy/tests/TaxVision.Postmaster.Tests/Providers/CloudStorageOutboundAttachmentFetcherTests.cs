using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class CloudStorageOutboundAttachmentFetcherTests
{
    /// <summary>Encola respuestas HTTP en orden — download-url y luego bytes presignados, por cada adjunto.</summary>
    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public void Enqueue(HttpResponseMessage response) => _responses.Enqueue(response);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(
                _responses.Count > 0
                    ? _responses.Dequeue()
                    : new HttpResponseMessage(HttpStatusCode.InternalServerError)
            );
    }

    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("No HTTP call should happen for an empty attachment list.");
    }

    /// <summary>Fase 13 (hardening) — simula una caída de red/timeout de CloudStorage lanzando la excepción indicada en cada llamada HTTP.</summary>
    private sealed class FaultingHandler(Func<Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw exceptionFactory();
    }

    private sealed class FakeTokenAcquirer(string? token = "fake-token") : IPostmasterServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) => Task.FromResult(token);
    }

    [Fact]
    public async Task FetchAllAsync_WithEmptyList_ReturnsSuccessWithoutCallingHttp()
    {
        var httpClient = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value);
    }

    [Fact]
    public async Task FetchAllAsync_WithoutServiceToken_FailsWithoutCallingHttp()
    {
        var httpClient = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(token: null),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "application/pdf", 2048).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentFetcher.Auth", result.Error.Code);
    }

    [Fact]
    public async Task FetchAllAsync_WithAValidAttachment_DownloadsTheUrlThenTheBytes()
    {
        var handler = new QueuedHandler();
        var fileId = Guid.NewGuid();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"fileId":"{{fileId}}","downloadUrl":"http://localhost:5210/presigned","expiresAtUtc":"2026-07-18T00:00:00Z"}"""
                ),
            }
        );
        var fileBytes = Encoding.UTF8.GetBytes("%PDF-1.4 fake");
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fileBytes) });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef.Create(fileId, "invoice.pdf", "application/pdf", fileBytes.Length).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsSuccess);
        var fetched = Assert.Single(result.Value);
        Assert.Equal("invoice.pdf", fetched.Filename);
        Assert.Equal("application/pdf", fetched.ContentType);
        Assert.Equal(fileBytes, fetched.Content);
    }

    [Fact]
    public async Task FetchAllAsync_AcceptsAnAttachmentLargerThan5MB_NoDomainLevelCapLikeInlineAssets()
    {
        var handler = new QueuedHandler();
        var fileId = Guid.NewGuid();
        handler.Enqueue(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"fileId":"{{fileId}}","downloadUrl":"http://localhost:5210/presigned","expiresAtUtc":"2026-07-18T00:00:00Z"}"""
                ),
            }
        );
        var fileBytes = new byte[10 * 1024 * 1024];
        handler.Enqueue(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(fileBytes) });
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef
            .Create(fileId, "big.bin", "application/octet-stream", fileBytes.Length)
            .Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
    }

    /// <summary>Fase 13 (hardening) — antes de este fix una caída de red en el download-url subía sin atrapar hasta el middleware genérico (500); ahora resuelve a un Result.Failure limpio.</summary>
    [Fact]
    public async Task FetchAllAsync_WhenDownloadUrlCallThrowsHttpRequestException_ReturnsCleanFailure()
    {
        var handler = new FaultingHandler(() => new HttpRequestException("Connection refused."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "application/pdf", 2048).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentFetcher.Download", result.Error.Code);
    }

    /// <summary>Fase 13 (hardening) — TaskCanceledException cubre tanto cancelación explícita como el timeout de 30s del HttpClient; ambos deben resolver a Result.Failure, nunca propagar.</summary>
    [Fact]
    public async Task FetchAllAsync_WhenDownloadUrlCallThrowsTaskCanceledException_ReturnsCleanFailure()
    {
        var handler = new FaultingHandler(() => new TaskCanceledException("The request timed out."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef.Create(Guid.NewGuid(), "invoice.pdf", "application/pdf", 2048).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentFetcher.Download", result.Error.Code);
    }

    /// <summary>Fase 13 (hardening) — mismo caso que el download-url pero en la segunda llamada (el GET del presigned URL de MinIO).</summary>
    [Fact]
    public async Task FetchAllAsync_WhenPresignedDownloadThrowsHttpRequestException_ReturnsCleanFailure()
    {
        var fileId = Guid.NewGuid();
        var handler = new SequencedHandler(
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"fileId":"{{fileId}}","downloadUrl":"http://localhost:5210/presigned","expiresAtUtc":"2026-07-18T00:00:00Z"}"""
                ),
            },
            () => new HttpRequestException("MinIO unreachable.")
        );
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageOutboundAttachmentFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageOutboundAttachmentFetcher>.Instance
        );
        var attachment = OutboundAttachmentRef.Create(fileId, "invoice.pdf", "application/pdf", 2048).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [attachment], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("OutboundAttachmentFetcher.Download", result.Error.Code);
    }

    /// <summary>Primera llamada responde normalmente, la segunda lanza — usado para probar el fallo puntual del GET presignado.</summary>
    private sealed class SequencedHandler(HttpResponseMessage first, Func<Exception> thenThrow) : HttpMessageHandler
    {
        private int _callCount;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            _callCount++;
            return _callCount == 1 ? Task.FromResult(first) : throw thenThrow();
        }
    }
}
