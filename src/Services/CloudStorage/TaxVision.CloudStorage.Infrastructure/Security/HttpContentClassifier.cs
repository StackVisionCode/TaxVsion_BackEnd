using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Moderation;

namespace TaxVision.CloudStorage.Infrastructure.Security;

/// <summary>Conexión al servicio TaxVision.ContentScanner (red interna de Docker en producción).</summary>
public sealed class ContentScannerOptions
{
    public const string SectionName = "ContentScanner";

    /// <summary>Vacío = sin scanner; la política decide si eso deja los archivos en revisión (falla cerrada).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    public string ApiKey { get; set; } = string.Empty;
    public int TimeoutSeconds { get; set; } = 120;
    public int MaxAttempts { get; set; } = 3;
    public int RetryDelayMilliseconds { get; set; } = 1000;
}

/// <summary>
/// <see cref="IContentClassifier"/> por HTTP. Reintenta lo transitorio (ocupado, caído, timeout) y
/// después avisa <see cref="ContentClassifierUnavailableException"/>; un 422 es contenido indecodificable.
/// </summary>
public sealed class HttpContentClassifier(HttpClient http, IOptions<ContentScannerOptions> options) : IContentClassifier
{
    private const string ApiKeyHeader = "X-Scanner-Key";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(options.Value.BaseUrl);

    public async Task<ContentClassification> ClassifyAsync(
        Stream content,
        string contentType,
        ContentSampling sampling,
        CancellationToken ct
    )
    {
        if (!IsConfigured)
            throw new ContentClassifierUnavailableException("Content scanner is not configured.");
        if (!content.CanSeek)
            throw new ArgumentException("The content stream must be seekable to allow retries.", nameof(content));

        var settings = options.Value;
        var attempts = Math.Max(1, settings.MaxAttempts);
        var start = content.Position;
        Exception? last = null;

        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            if (attempt > 1)
                await Task.Delay(TimeSpan.FromMilliseconds(Math.Max(0, settings.RetryDelayMilliseconds) * (attempt - 1)), ct);
            content.Position = start;
            try
            {
                using var request = BuildRequest(settings, content, contentType, sampling);
                using var response = await http.SendAsync(request, ct);
                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                    throw new ContentNotDecodableException("The content scanner could not decode the file.");
                if (IsTransient(response.StatusCode))
                {
                    last = new HttpRequestException($"Content scanner answered {(int)response.StatusCode}.");
                    continue;
                }
                if (!response.IsSuccessStatusCode)
                    throw new ContentClassifierUnavailableException(
                        $"Content scanner rejected the request with {(int)response.StatusCode}."
                    );

                var body =
                    await response.Content.ReadFromJsonAsync<ScanResponse>(ct)
                    ?? throw new ContentClassifierUnavailableException("Content scanner returned an empty body.");
                return new ContentClassification(Math.Clamp(body.Nsfw, 0, 1), body.Frames);
            }
            catch (HttpRequestException ex)
            {
                last = ex;
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                last = ex; // timeout del HttpClient, no cancelación del llamador
            }
        }

        throw new ContentClassifierUnavailableException(
            $"Content scanner unavailable after {attempts} attempt(s).",
            last
        );
    }

    private static HttpRequestMessage BuildRequest(
        ContentScannerOptions settings,
        Stream content,
        string contentType,
        ContentSampling sampling
    )
    {
        var query = string.Create(
            CultureInfo.InvariantCulture,
            $"v1/scan?frameIntervalSeconds={sampling.FrameIntervalSeconds}&maxFrames={sampling.MaxFrames}"
        );
        var request = new HttpRequestMessage(HttpMethod.Post, new Uri(BaseUri(settings.BaseUrl), query))
        {
            Content = new LeaveOpenStreamContent(content, contentType),
        };
        request.Headers.Add(ApiKeyHeader, settings.ApiKey);
        return request;
    }

    private static Uri BaseUri(string baseUrl) => new(baseUrl.EndsWith('/') ? baseUrl : baseUrl + "/");

    private static bool IsTransient(HttpStatusCode status) =>
        status
            is HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.BadGateway
                or HttpStatusCode.GatewayTimeout
                or HttpStatusCode.RequestTimeout
                or HttpStatusCode.TooManyRequests
                or HttpStatusCode.InternalServerError;

    private sealed record ScanResponse(string? Kind, int Frames, double Nsfw, long ElapsedMs);

    /// <summary>El stream es del ScanFileHandler: se envía sin cerrarlo para poder reintentar.</summary>
    private sealed class LeaveOpenStreamContent : HttpContent
    {
        private readonly Stream _stream;

        public LeaveOpenStreamContent(Stream stream, string contentType)
        {
            _stream = stream;
            Headers.ContentType = MediaTypeHeaderValue.TryParse(contentType, out var parsed)
                ? parsed
                : new MediaTypeHeaderValue("application/octet-stream");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _stream.CopyToAsync(stream);

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context,
            CancellationToken cancellationToken
        ) => _stream.CopyToAsync(stream, cancellationToken);

        protected override bool TryComputeLength(out long length)
        {
            length = _stream.Length - _stream.Position;
            return true;
        }
    }
}
