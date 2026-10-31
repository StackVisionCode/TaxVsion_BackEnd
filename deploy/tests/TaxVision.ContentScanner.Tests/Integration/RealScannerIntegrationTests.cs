using System.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.ContentScanner.Scanning;

namespace TaxVision.ContentScanner.Tests.Integration;

/// <summary>
/// ffmpeg y el modelo reales (namespace Integration: fuera del CI). Solo imágenes y videos sintéticos neutros
/// generados con ffmpeg; nunca contenido real. Modelo: variable `CONTENT_SCANNER_TEST_MODEL` (si falta, se omite).
/// </summary>
public sealed class RealScannerIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "content-scanner-it-" + Guid.NewGuid().ToString("N"));

    public RealScannerIntegrationTests() => Directory.CreateDirectory(_dir);

    private static ContentScannerOptions Options(string modelPath = "") =>
        new()
        {
            ModelPath = modelPath,
            InputSize = 384,
            Mean = [0.5f, 0.5f, 0.5f],
            Std = [0.5f, 0.5f, 0.5f],
            NsfwLabelIndex = 1,
            FfmpegPath = "ffmpeg",
            FfmpegTimeoutSeconds = 60,
            MaxConcurrentScans = 1,
            QueueTimeoutSeconds = 30,
            InferenceThreads = 1,
            MinFrameIntervalSeconds = 1,
            MaxFrames = 30,
        };

    [Fact]
    public async Task Ffmpeg_decodes_an_image_to_one_square_rgb_frame()
    {
        var png = Generate("red.png", "-f", "lavfi", "-i", "color=c=red:s=64x48", "-frames:v", "1");
        var frames = await new FfmpegFrameExtractor(Microsoft.Extensions.Options.Options.Create(Options()))
            .ExtractAsync(png, MediaKind.Image, new FrameSampling(1, 1), CancellationToken.None);

        var frame = Assert.Single(frames);
        Assert.Equal(384 * 384 * 3, frame.Length);
        Assert.True(frame[0] > 200 && frame[1] < 40 && frame[2] < 40); // rojo
    }

    [Fact]
    public async Task Ffmpeg_samples_one_frame_every_interval_up_to_the_cap()
    {
        var mp4 = Generate("clip.mp4", "-f", "lavfi", "-i", "testsrc=duration=7:size=160x120:rate=10", "-pix_fmt", "yuv420p");
        var extractor = new FfmpegFrameExtractor(Microsoft.Extensions.Options.Options.Create(Options()));

        var everyTwo = await extractor.ExtractAsync(mp4, MediaKind.Video, new FrameSampling(2, 30), CancellationToken.None);
        var capped = await extractor.ExtractAsync(mp4, MediaKind.Video, new FrameSampling(1, 3), CancellationToken.None);

        Assert.InRange(everyTwo.Count, 3, 5);
        Assert.Equal(3, capped.Count);
    }

    [Fact]
    public async Task Garbage_is_reported_as_undecodable()
    {
        var path = Path.Combine(_dir, "junk.bin");
        await File.WriteAllBytesAsync(path, [0x00, 0x01, 0x02, 0x03, 0x04]);
        var extractor = new FfmpegFrameExtractor(Microsoft.Extensions.Options.Options.Create(Options()));
        await Assert.ThrowsAsync<UndecodableMediaException>(() =>
            extractor.ExtractAsync(path, MediaKind.Image, new FrameSampling(1, 1), CancellationToken.None));
    }

    [Fact]
    public async Task The_real_model_scores_a_neutral_test_pattern_as_safe()
    {
        var model = Environment.GetEnvironmentVariable("CONTENT_SCANNER_TEST_MODEL");
        if (string.IsNullOrWhiteSpace(model) || !File.Exists(model))
            return; // sin modelo local no hay nada que medir
        var options = Microsoft.Extensions.Options.Options.Create(Options(model));
        using var classifier = new OnnxNsfwClassifier(options, NullLogger<OnnxNsfwClassifier>.Instance);
        using var scanner = new ScanService(new FfmpegFrameExtractor(options), classifier, options, NullLogger<ScanService>.Instance);
        var png = Generate("pattern.png", "-f", "lavfi", "-i", "testsrc2=size=640x480", "-frames:v", "1");

        var result = await scanner.ScanAsync(png, MediaKind.Image, new FrameSampling(1, 1), CancellationToken.None);

        Assert.True(classifier.IsReady);
        Assert.InRange(result.Nsfw, 0f, 0.5f);
    }

    private string Generate(string name, params string[] args)
    {
        var path = Path.Combine(_dir, name);
        var psi = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-hide_banner", "-loglevel", "error", "-y" }.Concat(args).Append(path))
            psi.ArgumentList.Add(a);
        using var process = Process.Start(psi)!;
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return path;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // archivos temporales: no hacen fallar el test
        }
    }
}
