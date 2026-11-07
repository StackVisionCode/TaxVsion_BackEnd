using System.Globalization;
using Microsoft.Extensions.Options;
using TaxVision.ContentScanner;
using TaxVision.ContentScanner.Scanning;

// F8d/T-20 (D-65): scanner de contenido interno. Solo puntúa imágenes y cuadros de video; CloudStorage decide.
// Vive en la red interna de Docker sin puertos publicados en producción.
var builder = WebApplication.CreateBuilder(args);

builder
    .Services.AddOptions<ContentScannerOptions>()
    .Bind(builder.Configuration.GetSection(ContentScannerOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<INsfwClassifier, OnnxNsfwClassifier>();
builder.Services.AddSingleton<IFrameExtractor, FfmpegFrameExtractor>();
builder.Services.AddSingleton<ScanService>();
builder.Services.AddSingleton<ScannerApiKey>();
builder.Services.AddSingleton<ITempDiskSpace, DriveTempDiskSpace>();

var maxUploadBytes = builder.Configuration.GetValue<long>($"{ContentScannerOptions.SectionName}:MaxUploadBytes");
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUploadBytes);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet(
    "/health/ready",
    (INsfwClassifier classifier) =>
        classifier.IsReady
            ? Results.Ok(new { status = "ready" })
            : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.MapPost("/v1/scan", ScanEndpoint.HandleAsync).AddEndpointFilter(ScanEndpoint.RequireApiKey);

app.Run();

/// <summary>`POST /v1/scan`: cuerpo = bytes del archivo; `Content-Type` = tipo detectado por CloudStorage.</summary>
internal static class ScanEndpoint
{
    public const string ApiKeyHeader = "X-Scanner-Key";
    public const string ModelUnavailableReason = "model_unavailable";

    public static async ValueTask<object?> RequireApiKey(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next
    )
    {
        var key = context.HttpContext.RequestServices.GetRequiredService<ScannerApiKey>();
        var provided = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
        return key.Matches(provided) ? await next(context) : Results.Unauthorized();
    }

    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        INsfwClassifier classifier,
        ScanService scanner,
        ITempDiskSpace disk,
        IOptions<ContentScannerOptions> options,
        int? frameIntervalSeconds,
        int? maxFrames,
        CancellationToken ct
    )
    {
        // Sin modelo no hay escaneo posible: se avisa antes de tocar el cuerpo.
        if (!classifier.IsReady)
            return ModelUnavailable(request.HttpContext.Response, options.Value.ModelUnavailableRetryAfterSeconds);

        var mediaType = request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
        MediaKind? kind =
            mediaType.StartsWith("image/", StringComparison.Ordinal) ? MediaKind.Image
            : mediaType.StartsWith("video/", StringComparison.Ordinal) ? MediaKind.Video
            : null;
        if (kind is null)
            return Results.Problem(
                statusCode: StatusCodes.Status415UnsupportedMediaType,
                title: "Only images and videos are scanned."
            );

        if (request.ContentLength > options.Value.MaxUploadBytes)
            return Results.Problem(
                statusCode: StatusCodes.Status413PayloadTooLarge,
                title: "The file exceeds the upload limit."
            );

        string? path = null;
        try
        {
            // El turno va antes del disco: con todos ocupados no se escribe nada.
            await using var turn = await scanner.AcquireSlotAsync(ct);

            Directory.CreateDirectory(options.Value.TempDirectory);
            if (request.ContentLength > disk.AvailableBytes(options.Value.TempDirectory))
                return Busy();

            path = Path.Combine(options.Value.TempDirectory, $"{Guid.NewGuid():N}.bin");
            await using (var file = File.Create(path))
                await request.Body.CopyToAsync(file, ct);

            var result = await scanner.ScanAsync(path, kind.Value, scanner.Clamp(frameIntervalSeconds, maxFrames), ct);
            return Results.Ok(result);
        }
        catch (ScannerBusyException)
        {
            return Busy();
        }
        catch (UndecodableMediaException)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                title: "The file could not be decoded."
            );
        }
        finally
        {
            if (path is not null)
                File.Delete(path);
        }
    }

    private static IResult ModelUnavailable(HttpResponse response, int retryAfterSeconds)
    {
        response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
        return Results.Json(
            new { reason = ModelUnavailableReason },
            statusCode: StatusCodes.Status503ServiceUnavailable
        );
    }

    private static IResult Busy() =>
        Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Content scanner is busy.");
}

public partial class Program;
