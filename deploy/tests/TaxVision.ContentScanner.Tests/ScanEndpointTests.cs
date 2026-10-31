using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TaxVision.ContentScanner.Scanning;

namespace TaxVision.ContentScanner.Tests;

/// <summary>Contrato de `POST /v1/scan`: clave, tipos aceptados, peor cuadro, muestreo acotado y ocupado → 503.</summary>
public sealed class ScanEndpointTests : IClassFixture<ScanEndpointTests.Factory>
{
    private const string Key = "test-key-0123456789abcdef";
    private readonly Factory _factory;

    public ScanEndpointTests(Factory factory) => _factory = factory;

    [Fact]
    public async Task Without_the_key_returns_401()
    {
        var response = await _factory.CreateClient().PostAsync("/v1/scan", Body("image/png"));
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_pdf_is_not_scanned()
    {
        var response = await Client().PostAsync("/v1/scan", Body("application/pdf"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task A_video_reports_the_worst_frame_and_the_sampling_is_clamped()
    {
        _factory.Extractor.Frames = 3;
        _factory.Classifier.Scores = new Queue<float>([0.1f, 0.97f, 0.2f]);

        var response = await Client().PostAsync("/v1/scan?frameIntervalSeconds=0&maxFrames=500", Body("video/mp4"));

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<ScanResult>();
        Assert.Equal("video", result!.Kind);
        Assert.Equal(3, result.Frames);
        Assert.Equal(0.97f, result.Nsfw, 3);
        Assert.Equal(new FrameSampling(1, 30), _factory.Extractor.LastSampling);
    }

    [Fact]
    public async Task An_audio_only_webm_has_nothing_visual_and_is_not_sent_to_review()
    {
        _factory.Extractor.HasVideo = false;
        var callsBefore = _factory.Extractor.ExtractCalls;
        try
        {
            var response = await Client().PostAsync("/v1/scan", Body("video/webm"));

            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadFromJsonAsync<ScanResult>();
            Assert.Equal("audio", result!.Kind);
            Assert.Equal(0, result.Frames);
            Assert.Equal(0f, result.Nsfw);
            Assert.Equal(callsBefore, _factory.Extractor.ExtractCalls);
        }
        finally
        {
            _factory.Extractor.HasVideo = true;
        }
    }

    [Fact]
    public async Task Undecodable_content_returns_422()
    {
        _factory.Extractor.Fail = true;
        try
        {
            var response = await Client().PostAsync("/v1/scan", Body("image/jpeg"));
            Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        }
        finally
        {
            _factory.Extractor.Fail = false;
        }
    }

    private HttpClient Client()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Scanner-Key", Key);
        return client;
    }

    private static ByteArrayContent Body(string contentType)
    {
        var content = new ByteArrayContent([1, 2, 3]);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return content;
    }

    public sealed class Factory : WebApplicationFactory<Program>
    {
        public FakeExtractor Extractor { get; } = new();
        public FakeClassifier Classifier { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("ContentScanner:ApiKey", Key);
            builder.UseSetting(
                "ContentScanner:TempDirectory",
                Path.Combine(Path.GetTempPath(), "content-scanner-tests")
            );
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IFrameExtractor>();
                services.RemoveAll<INsfwClassifier>();
                services.AddSingleton<IFrameExtractor>(Extractor);
                services.AddSingleton<INsfwClassifier>(Classifier);
            });
        }
    }

    public sealed class FakeExtractor : IFrameExtractor
    {
        public int Frames { get; set; } = 1;
        public bool Fail { get; set; }
        public bool HasVideo { get; set; } = true;
        public int ExtractCalls { get; private set; }
        public FrameSampling? LastSampling { get; private set; }

        public Task<IReadOnlyList<byte[]>> ExtractAsync(
            string path,
            MediaKind kind,
            FrameSampling sampling,
            CancellationToken ct
        )
        {
            LastSampling = sampling;
            ExtractCalls++;
            if (Fail)
                throw new UndecodableMediaException("broken");
            IReadOnlyList<byte[]> frames = Enumerable.Range(0, Frames).Select(_ => new byte[3]).ToList();
            return Task.FromResult(frames);
        }

        public Task<bool> HasVideoStreamAsync(string path, CancellationToken ct) => Task.FromResult(HasVideo);
    }

    public sealed class FakeClassifier : INsfwClassifier
    {
        public Queue<float> Scores { get; set; } = new();
        public bool IsReady => true;

        public float Score(ReadOnlySpan<byte> rgb) => Scores.Count > 0 ? Scores.Dequeue() : 0f;
    }
}
