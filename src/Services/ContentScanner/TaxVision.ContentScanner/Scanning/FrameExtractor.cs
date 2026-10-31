using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace TaxVision.ContentScanner.Scanning;

public enum MediaKind
{
    Image,
    Video,
}

/// <summary>Muestreo de video pedido por CloudStorage (ya acotado a los topes técnicos).</summary>
public sealed record FrameSampling(int IntervalSeconds, int MaxFrames);

/// <summary>Decodifica una imagen o muestrea un video a cuadros RGB cuadrados del tamaño del modelo.</summary>
public interface IFrameExtractor
{
    Task<IReadOnlyList<byte[]>> ExtractAsync(string path, MediaKind kind, FrameSampling sampling, CancellationToken ct);
}

/// <summary>Contenido que ffmpeg no pudo decodificar (corrupto o formato no soportado).</summary>
public sealed class UndecodableMediaException(string message) : Exception(message);

/// <summary>
/// ffmpeg para imágenes y videos: un solo decodificador para todos los formatos, sin librerías de imagen extra.
/// Solo lee el archivo local (`-protocol_whitelist file`): un contenedor que apunte a URLs (p. ej. una lista HLS)
/// no puede hacer que el scanner salga a la red.
/// </summary>
public sealed class FfmpegFrameExtractor(IOptions<ContentScannerOptions> options) : IFrameExtractor
{
    private readonly ContentScannerOptions _options = options.Value;

    public async Task<IReadOnlyList<byte[]>> ExtractAsync(
        string path,
        MediaKind kind,
        FrameSampling sampling,
        CancellationToken ct
    )
    {
        var size = _options.InputSize;
        var frameBytes = size * size * 3;
        var maxFrames = kind == MediaKind.Image ? 1 : sampling.MaxFrames;

        var psi = new ProcessStartInfo(_options.FfmpegPath)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in BuildArguments(path, kind, sampling, size))
            psi.ArgumentList.Add(arg);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.FfmpegTimeoutSeconds));
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("ffmpeg could not be started.");
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try
        {
            var frames = new List<byte[]>();
            var stdout = process.StandardOutput.BaseStream;
            while (frames.Count < maxFrames)
            {
                var frame = new byte[frameBytes];
                var read = await ReadFullAsync(stdout, frame, timeout.Token);
                if (read < frameBytes)
                    break;
                frames.Add(frame);
            }
            await process.WaitForExitAsync(timeout.Token);
            if (frames.Count == 0)
                throw new UndecodableMediaException($"ffmpeg produced no frames: {Trim(await stderr)}");
            return frames;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new UndecodableMediaException("ffmpeg timed out.");
        }
        finally
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
    }

    internal static IReadOnlyList<string> BuildArguments(string path, MediaKind kind, FrameSampling sampling, int size)
    {
        var scale = $"scale={size}:{size}:flags=bilinear";
        var filter =
            kind == MediaKind.Video
                ? $"fps=1/{sampling.IntervalSeconds.ToString(CultureInfo.InvariantCulture)},{scale}"
                : scale;
        var frames = (kind == MediaKind.Video ? sampling.MaxFrames : 1).ToString(CultureInfo.InvariantCulture);
        return
        [
            "-hide_banner",
            "-loglevel",
            "error",
            "-nostdin",
            "-protocol_whitelist",
            "file",
            "-i",
            path,
            "-an",
            "-sn",
            "-vf",
            filter,
            "-frames:v",
            frames,
            "-f",
            "rawvideo",
            "-pix_fmt",
            "rgb24",
            "pipe:1",
        ];
    }

    private static async Task<int> ReadFullAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct);
            if (read == 0)
                break;
            total += read;
        }
        return total;
    }

    private static string Trim(string text) => text.Length <= 300 ? text.Trim() : text[..300].Trim();
}
