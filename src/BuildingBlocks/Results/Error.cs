using System.Text.Json.Serialization;

namespace BuildingBlocks.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Espera real antes de reintentar, para errores de throttle de dominio. Viaja en el body
    /// como <c>retryAfterSeconds</c> y en la cabecera <c>Retry-After</c>, igual que los limiters HTTP.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }

    public Error WithRetryAfter(TimeSpan wait) =>
        this with
        {
            RetryAfterSeconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds)),
        };
}
