using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
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

    [Fact]
    public async Task Scan_AllSlotsBusy_Returns503WithoutWritingTempFile()
    {
        var tempDirectory = NewTempDirectory();
        var extractor = new GatedExtractor();
        using var tight = Tight(tempDirectory, extractor);
        var client = KeyedClient(tight);

        var holding = client.PostAsync("/v1/scan", Body("image/png"));
        await extractor.Entered.Task.WaitAsync(TimeSpan.FromSeconds(30));

        var waiting = client.PostAsync("/v1/scan", Body("image/png"));
        var mostFiles = 0;
        while (!waiting.IsCompleted)
        {
            mostFiles = Math.Max(mostFiles, Directory.GetFiles(tempDirectory).Length);
            await Task.Delay(10);
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await waiting).StatusCode);
        Assert.Equal(1, mostFiles);

        extractor.Open();
        (await holding).EnsureSuccessStatusCode();
        Assert.Empty(Directory.GetFiles(tempDirectory));
    }

    [Fact]
    public async Task Scan_FreesItsSlotForTheNextRequest()
    {
        var extractor = new GatedExtractor();
        extractor.Open();
        using var tight = Tight(NewTempDirectory(), extractor);
        var client = KeyedClient(tight);

        (await client.PostAsync("/v1/scan", Body("image/png"))).EnsureSuccessStatusCode();
        (await client.PostAsync("/v1/scan", Body("image/png"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Scan_ContentLengthExceedsFreeSpace_Returns503()
    {
        var tempDirectory = NewTempDirectory();
        using var tight = Tight(tempDirectory, new GatedExtractor(), freeBytes: 2);

        var response = await KeyedClient(tight).PostAsync("/v1/scan", Body("image/png"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Empty(Directory.GetFiles(tempDirectory));
    }

    [Fact]
    public async Task Scan_ContentLengthOverTheUploadLimit_Returns413()
    {
        var extractor = new GatedExtractor();
        extractor.Open();
        using var tight = Tight(NewTempDirectory(), extractor)
            .WithWebHostBuilder(host => host.UseSetting("ContentScanner:MaxUploadBytes", "1024"));
        var content = new ByteArrayContent(new byte[2048]);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");

        var response = await KeyedClient(tight).PostAsync("/v1/scan", content);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task Scan_ModelNotLoaded_Returns503WithoutReadingBody()
    {
        var tempDirectory = NewTempDirectory();
        var body = new BodyProbe();
        using var noModel = _factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("ContentScanner:TempDirectory", tempDirectory);
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<INsfwClassifier>();
                services.AddSingleton<INsfwClassifier>(new UnloadedClassifier());
                services.AddSingleton<IStartupFilter>(body);
            });
        });

        var response = await KeyedClient(noModel).PostAsync("/v1/scan", Body("image/png"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("model_unavailable", await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.RetryAfter);
        Assert.False(body.WasRead);
        Assert.Empty(Directory.GetFiles(tempDirectory));
    }

    [Fact]
    public async Task Scan_KeyOfDifferentLength_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Scanner-Key", Key + "-longer");

        var response = await client.PostAsync("/v1/scan", Body("image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scan_KeyOfSameLengthButWrong_Returns401()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Scanner-Key", new string('x', Key.Length));

        var response = await client.PostAsync("/v1/scan", Body("image/png"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Scan_CorrectKey_Passes()
    {
        var response = await Client().PostAsync("/v1/scan", Body("image/png"));

        response.EnsureSuccessStatusCode();
    }

    private HttpClient Client() => KeyedClient(_factory);

    private static HttpClient KeyedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Scanner-Key", Key);
        return client;
    }

    // Un solo turno, espera corta y su propio directorio para contar lo que se escribe.
    private WebApplicationFactory<Program> Tight(
        string tempDirectory,
        IFrameExtractor extractor,
        long? freeBytes = null
    ) =>
        _factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("ContentScanner:TempDirectory", tempDirectory);
            host.UseSetting("ContentScanner:MaxConcurrentScans", "1");
            host.UseSetting("ContentScanner:QueueTimeoutSeconds", "1");
            host.ConfigureTestServices(services =>
            {
                services.RemoveAll<IFrameExtractor>();
                services.AddSingleton(extractor);
                if (freeBytes is { } bytes)
                {
                    services.RemoveAll<ITempDiskSpace>();
                    services.AddSingleton<ITempDiskSpace>(new FixedDiskSpace(bytes));
                }
            });
        });

    private static string NewTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "content-scanner-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
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

    private sealed class UnloadedClassifier : INsfwClassifier
    {
        public bool IsReady => false;

        public float Score(ReadOnlySpan<byte> rgb) =>
            throw new InvalidOperationException("Content scanner model is not loaded.");
    }

    /// <summary>Envuelve el cuerpo de la petición para saber si alguien lo leyó.</summary>
    private sealed class BodyProbe : IStartupFilter
    {
        public bool WasRead { get; private set; }

        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
            app =>
            {
                app.Use(
                    (context, nextMiddleware) =>
                    {
                        context.Request.Body = new ProbeStream(context.Request.Body, () => WasRead = true);
                        return nextMiddleware();
                    }
                );
                next(app);
            };

        private sealed class ProbeStream(Stream inner, Action onRead) : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                onRead();
                return inner.Read(buffer, offset, count);
            }

            public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
            {
                onRead();
                return inner.ReadAsync(buffer, ct);
            }

            public override void Flush() { }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }

    private sealed class FixedDiskSpace(long bytes) : ITempDiskSpace
    {
        public long AvailableBytes(string directory) => bytes;
    }

    private sealed class GatedExtractor : IFrameExtractor
    {
        private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Open() => _open.TrySetResult();

        public async Task<IReadOnlyList<byte[]>> ExtractAsync(
            string path,
            MediaKind kind,
            FrameSampling sampling,
            CancellationToken ct
        )
        {
            Entered.TrySetResult();
            await _open.Task.WaitAsync(ct);
            return [new byte[3]];
        }

        public Task<bool> HasVideoStreamAsync(string path, CancellationToken ct) => Task.FromResult(true);
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
