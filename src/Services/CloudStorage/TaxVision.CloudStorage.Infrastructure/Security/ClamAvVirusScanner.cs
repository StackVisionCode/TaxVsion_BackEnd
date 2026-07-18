using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Infrastructure.Security;

public sealed class ClamAvOptions
{
    public const string SectionName = "ClamAv";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 120;
    public int ChunkSizeBytes { get; set; } = 64 * 1024;
}

public sealed class ClamAvVirusScanner(IOptions<ClamAvOptions> options) : IVirusScanner
{
    public async Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct)
    {
        var config = options.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(config.TimeoutSeconds, 5, 600)));

        using var client = new TcpClient();
        await client.ConnectAsync(config.Host, config.Port, timeout.Token);
        await using var network = client.GetStream();
        await network.WriteAsync("zINSTREAM\0"u8.ToArray(), timeout.Token);

        var buffer = new byte[Math.Clamp(config.ChunkSizeBytes, 4096, 1024 * 1024)];
        var lengthPrefix = new byte[4];
        int read;
        while ((read = await content.ReadAsync(buffer, timeout.Token)) > 0)
        {
            BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, read);
            await network.WriteAsync(lengthPrefix, timeout.Token);
            await network.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
        }

        Array.Clear(lengthPrefix);
        await network.WriteAsync(lengthPrefix, timeout.Token);
        await network.FlushAsync(timeout.Token);

        var responseBuffer = new byte[4096];
        var responseLength = await network.ReadAsync(responseBuffer, timeout.Token);
        var response = Encoding.UTF8.GetString(responseBuffer, 0, responseLength).TrimEnd('\0', '\r', '\n');

        if (response.EndsWith("OK", StringComparison.OrdinalIgnoreCase))
            return VirusScanResult.Clean(response);
        if (response.Contains("FOUND", StringComparison.OrdinalIgnoreCase))
            return VirusScanResult.Infected(response);
        return VirusScanResult.Error(response);
    }
}
