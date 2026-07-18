namespace TaxVision.Connectors.Infrastructure.RateLimit;

/// <summary>Default 20/min — bien debajo del techo duro de Graph (30/min/buzón) y del derivado de Gmail (~60/min/usuario), D3 §3.5/§7 — el margen es intencional.</summary>
public sealed class SendRateLimiterOptions
{
    public int MaxRequestsPerMinute { get; set; } = 20;
}
