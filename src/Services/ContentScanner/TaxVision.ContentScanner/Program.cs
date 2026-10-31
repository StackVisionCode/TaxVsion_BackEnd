using System.Security.Cryptography;
using System.Text;
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

var maxUploadBytes = builder.Configuration.GetValue<long>($"{ContentScannerOptions.SectionName}:MaxUploadBytes");
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = maxUploadBytes);

var app = builder.Build();

app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet(
    "/health/ready",
    (INsfwClassifier classifier) => classifier.IsReady ? Results.Ok(new { status = "ready" }) : Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
);

app.MapPost("/v1/scan", ScanEndpoint.HandleAsync).AddEndpointFilter(ScanEndpoint.RequireApiKey);

app.Run();

/// <summary>`POST /v1/scan`: cuerpo = bytes del archivo; `Content-Type` = tipo detectado por CloudStorage.</summary>
internal static class ScanEndpoint
{
    public const string ApiKeyHeader = "X-Scanner-Key";

    public static async ValueTask<object?> RequireApiKey(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var expected = context.HttpContext.RequestServices.GetRequiredService<IOptions<ContentScannerOptions>>().Value.ApiKey;
        var provided = context.HttpContext.Request.Headers[ApiKeyHeader].ToString();
        // Comparación en tiempo constante: el tiempo de respuesta no revela cuánto de la clave coincide.
        var ok = CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(expected));
        return ok ? await next(context) : Results.Unauthorized();
    }

    public static async Task<IResult> HandleAsync(
        HttpRequest request,
        ScanService scanner,
        IOptions<ContentScannerOptions> options,
        int? frameIntervalSeconds,
        int? maxFrames,
        CancellationToken ct
    )
    {
        var mediaType = request.ContentType?.Split(';')[0].Trim().ToLowerInvariant() ?? string.Empty;
        MediaKind? kind = mediaType.StartsWith("image/", StringComparison.Ordinal) ? MediaKind.Image
            : mediaType.StartsWith("video/", StringComparison.Ordinal) ? MediaKind.Video
            : null;
        if (kind is null)
            return Results.Problem(statusCode: StatusCodes.Status415UnsupportedMediaType, title: "Only images and videos are scanned.");

        Directory.CreateDirectory(options.Value.TempDirectory);
        var path = Path.Combine(options.Value.TempDirectory, $"{Guid.NewGuid():N}.bin");
        try
        {
            await using (var file = File.Create(path))
                await request.Body.CopyToAsync(file, ct);

            var result = await scanner.ScanAsync(path, kind.Value, scanner.Clamp(frameIntervalSeconds, maxFrames), ct);
            return Results.Ok(result);
        }
        catch (ScannerBusyException)
        {
            return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Content scanner is busy.");
        }
        catch (UndecodableMediaException)
        {
            return Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: "The file could not be decoded.");
        }
        finally
        {
            File.Delete(path);
        }
    }
}

public partial class Program;
