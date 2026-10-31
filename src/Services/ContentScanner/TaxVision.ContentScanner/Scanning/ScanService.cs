using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace TaxVision.ContentScanner.Scanning;

/// <summary>Resultado: la peor puntuación sexual entre los cuadros revisados.</summary>
public sealed record ScanResult(string Kind, int Frames, float Nsfw, long ElapsedMs);

/// <summary>El scanner está ocupado (todos los turnos tomados): CloudStorage reintenta.</summary>
public sealed class ScannerBusyException() : Exception("Content scanner is busy.");

/// <summary>
/// Orquesta un escaneo con concurrencia acotada (la CPU del host es compartida con el resto de la plataforma).
/// </summary>
public sealed class ScanService(
    IFrameExtractor extractor,
    INsfwClassifier classifier,
    IOptions<ContentScannerOptions> options,
    ILogger<ScanService> logger
) : IDisposable
{
    private readonly ContentScannerOptions _options = options.Value;
    private readonly SemaphoreSlim _turns = new(options.Value.MaxConcurrentScans, options.Value.MaxConcurrentScans);

    public FrameSampling Clamp(int? intervalSeconds, int? maxFrames) =>
        new(
            Math.Max(intervalSeconds ?? _options.MinFrameIntervalSeconds, _options.MinFrameIntervalSeconds),
            Math.Clamp(maxFrames ?? _options.MaxFrames, 1, _options.MaxFrames)
        );

    public async Task<ScanResult> ScanAsync(string path, MediaKind kind, FrameSampling sampling, CancellationToken ct)
    {
        if (!await _turns.WaitAsync(TimeSpan.FromSeconds(_options.QueueTimeoutSeconds), ct))
            throw new ScannerBusyException();
        try
        {
            var watch = Stopwatch.StartNew();
            var frames = await extractor.ExtractAsync(path, kind, sampling, ct);
            var worst = 0f;
            foreach (var frame in frames)
            {
                ct.ThrowIfCancellationRequested();
                worst = Math.Max(worst, classifier.Score(frame));
            }
            watch.Stop();
            logger.LogInformation(
                "Scanned {Kind} with {Frames} frame(s) in {ElapsedMs} ms",
                kind,
                frames.Count,
                watch.ElapsedMilliseconds
            );
            return new ScanResult(
                kind == MediaKind.Video ? "video" : "image",
                frames.Count,
                worst,
                watch.ElapsedMilliseconds
            );
        }
        finally
        {
            _turns.Release();
        }
    }

    public void Dispose() => _turns.Dispose();
}
