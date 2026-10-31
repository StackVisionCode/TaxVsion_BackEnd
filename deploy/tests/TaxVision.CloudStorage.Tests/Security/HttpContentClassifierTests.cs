using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Infrastructure.Security;

namespace TaxVision.CloudStorage.Tests.Security;

/// <summary>Contrato con TaxVision.ContentScanner: clave, muestreo, reintentos y el stream que no se cierra.</summary>
public sealed class HttpContentClassifierTests
{
    private static readonly ContentSampling Sampling = new(2, 30);

    private static HttpContentClassifier Create(StubHandler handler, string baseUrl = "http://scanner:8080") =>
        new(
            new HttpClient(handler),
            Options.Create(
                new ContentScannerOptions
                {
                    BaseUrl = baseUrl,
                    ApiKey = "key-from-secrets-0123456789",
                    MaxAttempts = 3,
                    RetryDelayMilliseconds = 0,
                }
            )
        );

    [Fact]
    public async Task Sends_the_key_the_detected_type_and_the_sampling_and_reads_the_score()
    {
        var handler = new StubHandler(_ => Json("""{"kind":"video","frames":4,"nsfw":0.42,"elapsedMs":10}"""));
        await using var content = new MemoryStream([1, 2, 3]);

        var result = await Create(handler).ClassifyAsync(content, "video/mp4", Sampling, CancellationToken.None);

        Assert.Equal(0.42, result.SexualScore, 3);
        Assert.Equal(4, result.Frames);
        var request = handler.Requests.Single();
        Assert.Equal("http://scanner:8080/v1/scan?frameIntervalSeconds=2&maxFrames=30", request.Uri);
        Assert.Equal("key-from-secrets-0123456789", request.Key);
        Assert.Equal("video/mp4", request.ContentType);
        Assert.Equal([1, 2, 3], request.Body);
        Assert.True(content.CanRead); // el stream es del ScanFileHandler: sigue abierto
    }

    [Fact]
    public async Task A_busy_scanner_is_retried_with_the_whole_body()
    {
        var calls = 0;
        var handler = new StubHandler(_ =>
            ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) : Json("""{"frames":1,"nsfw":0.1}""")
        );

        var result = await Create(handler).ClassifyAsync(new MemoryStream([7, 8]), "image/png", Sampling, CancellationToken.None);

        Assert.Equal(0.1, result.SexualScore, 3);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Equal([7, 8], r.Body));
    }

    [Fact]
    public async Task Persistent_failures_surface_as_unavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<ContentClassifierUnavailableException>(() =>
            Create(handler).ClassifyAsync(new MemoryStream([1]), "image/png", Sampling, CancellationToken.None)
        );
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task Undecodable_is_not_retried_and_a_rejected_key_is_unavailable()
    {
        var undecodable = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.UnprocessableEntity));
        await Assert.ThrowsAsync<ContentNotDecodableException>(() =>
            Create(undecodable).ClassifyAsync(new MemoryStream([1]), "image/png", Sampling, CancellationToken.None)
        );
        Assert.Single(undecodable.Requests);

        var unauthorized = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        await Assert.ThrowsAsync<ContentClassifierUnavailableException>(() =>
            Create(unauthorized).ClassifyAsync(new MemoryStream([1]), "image/png", Sampling, CancellationToken.None)
        );
        Assert.Single(unauthorized.Requests);
    }

    [Fact]
    public void Without_a_base_url_it_reports_not_configured()
    {
        Assert.False(Create(new StubHandler(_ => Json("{}")), baseUrl: "").IsConfigured);
    }

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Uri, string? Key, string? ContentType, byte[] Body);

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(ct);
            Requests.Add(
                new CapturedRequest(
                    request.RequestUri!.ToString(),
                    request.Headers.TryGetValues("X-Scanner-Key", out var keys) ? keys.Single() : null,
                    request.Content?.Headers.ContentType?.MediaType,
                    body
                )
            );
            return respond(request);
        }
    }
}
