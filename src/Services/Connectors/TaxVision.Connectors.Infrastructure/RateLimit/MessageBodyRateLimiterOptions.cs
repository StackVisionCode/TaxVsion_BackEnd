namespace TaxVision.Connectors.Infrastructure.RateLimit;

public sealed class MessageBodyRateLimiterOptions
{
    // Correspondence no guarda los cuerpos: cada apertura (o re-apertura) de un email consume uno.
    public int MaxRequestsPerMinute { get; set; } = 60;
}
