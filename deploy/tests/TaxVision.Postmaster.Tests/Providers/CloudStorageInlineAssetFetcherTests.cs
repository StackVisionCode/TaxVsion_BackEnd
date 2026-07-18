using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Postmaster.Domain.Sending;
using TaxVision.Postmaster.Infrastructure.Providers.Assets;

namespace TaxVision.Postmaster.Tests.Providers;

public sealed class CloudStorageInlineAssetFetcherTests
{
    private sealed class ThrowingHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw new InvalidOperationException("No HTTP call should happen when total size validation fails fast.");
    }

    /// <summary>Fase 13 (hardening) — simula una caída de red/timeout de CloudStorage lanzando la excepción indicada en cada llamada HTTP.</summary>
    private sealed class FaultingHandler(Func<Exception> exceptionFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            throw exceptionFactory();
    }

    private sealed class FakeTokenAcquirer : IPostmasterServiceTokenAcquirer
    {
        public Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult<string?>("fake-token");
    }

    [Fact]
    public async Task FetchAllAsync_rejects_total_size_over_5MB_without_calling_http()
    {
        var httpClient = new HttpClient(new ThrowingHandler()) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageInlineAssetFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageInlineAssetFetcher>.Instance
        );

        var assets = Enumerable
            .Range(0, 26)
            .Select(i => InlineAsset.Create($"logo-{i}", Guid.NewGuid(), "image/png", 200 * 1024).Value)
            .ToArray(); // 26 * 200KB > 5MB

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), assets, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAssetFetcher.TotalSizeExceeded", result.Error.Code);
    }

    /// <summary>Fase 13 (hardening) — antes de este fix una caída de red en el download-url subía sin atrapar hasta el middleware genérico (500); ahora resuelve a un Result.Failure limpio, mismo patrón que CloudStorageOutboundAttachmentFetcher.</summary>
    [Fact]
    public async Task FetchAllAsync_WhenDownloadUrlCallThrowsHttpRequestException_ReturnsCleanFailure()
    {
        var handler = new FaultingHandler(() => new HttpRequestException("Connection refused."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageInlineAssetFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageInlineAssetFetcher>.Instance
        );
        var asset = InlineAsset.Create("logo-1", Guid.NewGuid(), "image/png", 200 * 1024).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [asset], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAssetFetcher.Download", result.Error.Code);
    }

    /// <summary>Fase 13 (hardening) — TaskCanceledException cubre tanto cancelación explícita como el timeout de 30s del HttpClient; ambos deben resolver a Result.Failure, nunca propagar.</summary>
    [Fact]
    public async Task FetchAllAsync_WhenDownloadUrlCallThrowsTaskCanceledException_ReturnsCleanFailure()
    {
        var handler = new FaultingHandler(() => new TaskCanceledException("The request timed out."));
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("http://localhost:5210/") };
        var fetcher = new CloudStorageInlineAssetFetcher(
            httpClient,
            new FakeTokenAcquirer(),
            NullLogger<CloudStorageInlineAssetFetcher>.Instance
        );
        var asset = InlineAsset.Create("logo-1", Guid.NewGuid(), "image/png", 200 * 1024).Value;

        var result = await fetcher.FetchAllAsync(Guid.NewGuid(), [asset], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("InlineAssetFetcher.Download", result.Error.Code);
    }
}
