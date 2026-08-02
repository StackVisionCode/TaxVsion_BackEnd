namespace TaxVision.Connectors.Infrastructure.RateLimit;

public sealed class ProviderRateLimiterOptions
{
    /// <summary>Cupo global compartido entre TODOS los tenants — protege al provider externo (Gmail/Graph).</summary>
    public int MaxRequestsPerSecond { get; set; } = 10;

    /// <summary>
    /// Rate Limit Fase 0.3 — cupo por tenant sobre el mismo cupo global, para que un tenant ruidoso
    /// no consuma todo el presupuesto y apague al resto (noisy neighbor). Default conservador:
    /// menos de un tercio del cupo global, ajustable por config sin tocar código.
    /// </summary>
    public int MaxRequestsPerSecondPerTenant { get; set; } = 3;
}
