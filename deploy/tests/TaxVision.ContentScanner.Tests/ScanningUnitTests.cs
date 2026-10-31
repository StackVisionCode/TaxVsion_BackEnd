using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.ContentScanner.Scanning;

namespace TaxVision.ContentScanner.Tests;

/// <summary>Preprocesado del modelo, softmax, argumentos seguros de ffmpeg y la cola con turnos.</summary>
public sealed class ScanningUnitTests
{
    [Fact]
    public void Preprocess_maps_bytes_to_minus_one_one_in_planar_order()
    {
        // Cuadro 1x1: R=0, G=255, B=127.5 aprox → -1, 1, ~0 con mean=std=0.5.
        var data = OnnxNsfwClassifier.Preprocess([0, 255, 128], 1, [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f]);
        Assert.Equal(-1f, data[0], 3);
        Assert.Equal(1f, data[1], 3);
        Assert.Equal(0.0039f, data[2], 3);
    }

    [Fact]
    public void Preprocess_rejects_a_frame_of_the_wrong_size() =>
        Assert.Throws<ArgumentException>(() =>
            OnnxNsfwClassifier.Preprocess(new byte[5], 2, [0.5f, 0.5f, 0.5f], [0.5f, 0.5f, 0.5f])
        );

    [Fact]
    public void Softmax_is_stable_with_large_logits()
    {
        var p = OnnxNsfwClassifier.Softmax([1000f, 1002f]);
        Assert.Equal(1f, p.Sum(), 4);
        Assert.True(p[1] > 0.88f && p[1] < 0.89f);
    }

    [Fact]
    public void Ffmpeg_reads_only_the_local_file_and_samples_videos()
    {
        var video = FfmpegFrameExtractor.BuildArguments("/tmp/x.bin", MediaKind.Video, new FrameSampling(2, 12), 384);
        Assert.Contains("-protocol_whitelist", video);
        Assert.Equal("file", video[video.ToList().IndexOf("-protocol_whitelist") + 1]);
        Assert.Contains("fps=1/2,scale=384:384:flags=bilinear", video);
        Assert.Equal("12", video[video.ToList().IndexOf("-frames:v") + 1]);

        var image = FfmpegFrameExtractor.BuildArguments("/tmp/x.bin", MediaKind.Image, new FrameSampling(2, 12), 384);
        Assert.Equal("1", image[image.ToList().IndexOf("-frames:v") + 1]);
    }

    [Fact]
    public void Ffprobe_reads_only_the_local_file_and_lists_video_streams()
    {
        var probe = FfmpegFrameExtractor.BuildProbeArguments("/tmp/x.bin").ToList();
        Assert.Equal("file", probe[probe.IndexOf("-protocol_whitelist") + 1]);
        Assert.Equal("v", probe[probe.IndexOf("-select_streams") + 1]);
        Assert.Equal("/tmp/x.bin", probe[^1]);
    }

    [Fact]
    public async Task With_every_turn_taken_the_scan_reports_busy()
    {
        var gate = new TaskCompletionSource();
        var options = Options.Create(
            new ContentScannerOptions
            {
                MaxConcurrentScans = 1,
                QueueTimeoutSeconds = 1,
                MinFrameIntervalSeconds = 1,
                MaxFrames = 30,
            }
        );
        var service = new ScanService(
            new BlockingExtractor(gate.Task),
            new ZeroClassifier(),
            options,
            NullLogger<ScanService>.Instance
        );

        var first = service.ScanAsync("a", MediaKind.Image, new FrameSampling(1, 1), CancellationToken.None);
        await Assert.ThrowsAsync<ScannerBusyException>(() =>
            service.ScanAsync("b", MediaKind.Image, new FrameSampling(1, 1), CancellationToken.None)
        );

        gate.SetResult();
        Assert.Equal(0f, (await first).Nsfw);
    }

    private sealed class BlockingExtractor(Task gate) : IFrameExtractor
    {
        public async Task<IReadOnlyList<byte[]>> ExtractAsync(
            string path,
            MediaKind kind,
            FrameSampling sampling,
            CancellationToken ct
        )
        {
            await gate;
            return [new byte[3]];
        }

        public Task<bool> HasVideoStreamAsync(string path, CancellationToken ct) => Task.FromResult(true);
    }

    private sealed class ZeroClassifier : INsfwClassifier
    {
        public bool IsReady => true;

        public float Score(ReadOnlySpan<byte> rgb) => 0f;
    }
}
