using System.Globalization;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace BuildingBlocks.Web.RateLimiting;

/// <summary>
/// Contrato único de un rechazo por rate limit, igual para el evaluador tiered, los limiters nativos
/// de ASP.NET Core de cada servicio y el Gateway: <c>429</c>, header <c>Retry-After</c> en segundos y
/// body <c>{ code: "RateLimit.Exceeded", message, retryAfterSeconds, policy?, layer? }</c>. Antes cada
/// limiter nativo devolvía un 429 vacío (algunos sin Retry-After) y el front no podía decirle al
/// usuario cuánto esperar.
/// </summary>
public static class RateLimitRejection
{
    public const string Code = "RateLimit.Exceeded";

    /// <summary>Espera informada cuando el limiter no dice cuándo se libera un permiso.</summary>
    public const int DefaultRetryAfterSeconds = 60;

    /// <summary>
    /// Clave de <see cref="HttpContext.Items"/> con la que un limiter sin
    /// <see cref="EnableRateLimitingAttribute"/> (el global del Gateway) nombra su política.
    /// </summary>
    public const string PolicyItemKey = "TaxVision.RateLimit.Policy";

    public static string Message(int retryAfterSeconds) =>
        $"You're making requests too quickly. Please try again in {FormatSeconds(retryAfterSeconds)}.";

    public static async Task WriteAsync(
        HttpContext context,
        int retryAfterSeconds,
        string? policy = null,
        string? layer = null,
        CancellationToken cancellationToken = default
    )
    {
        var seconds = Math.Max(1, retryAfterSeconds);
        var response = context.Response;

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        await response
            .WriteAsJsonAsync(
                new RateLimitRejectionBody(Code, Message(seconds), seconds, policy, layer),
                cancellationToken
            )
            .ConfigureAwait(false);
    }

    /// <summary>
    /// <c>OnRejected</c> de los limiters nativos: toma la espera real del lease (fixed/sliding window y
    /// token bucket la informan) y el nombre de la política del endpoint o de
    /// <see cref="PolicyItemKey"/>.
    /// </summary>
    public static ValueTask OnRejected(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var seconds = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter)
            ? (int)Math.Ceiling(retryAfter.TotalSeconds)
            : DefaultRetryAfterSeconds;

        var httpContext = context.HttpContext;
        var policy =
            httpContext.GetEndpoint()?.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName
            ?? httpContext.Items[PolicyItemKey] as string;

        NativeRateLimitMetrics.RecordRejected(policy);
        return new ValueTask(WriteAsync(httpContext, seconds, policy, layer: null, cancellationToken));
    }

    /// <summary>Configura el status y el <c>OnRejected</c> del contrato común en un <c>AddRateLimiter</c>.</summary>
    public static RateLimiterOptions UseTaxVisionRejectionResponse(this RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = OnRejected;
        return options;
    }

    private static string FormatSeconds(int seconds) => seconds == 1 ? "1 second" : $"{seconds} seconds";
}

public sealed record RateLimitRejectionBody(
    string Code,
    string Message,
    int RetryAfterSeconds,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Policy,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Layer
);
