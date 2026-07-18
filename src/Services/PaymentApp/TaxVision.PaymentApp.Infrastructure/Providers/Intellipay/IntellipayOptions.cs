namespace TaxVision.PaymentApp.Infrastructure.Providers.Intellipay;

/// <summary>
/// Config productiva de Intellipay. El operador de la plataforma solo coloca
/// <see cref="MerchantKey"/> y <see cref="ApiKey"/> — todo lo demás (endpoint, timeouts,
/// reintentos) queda con defaults sensatos hardcoded, override-ables solo para casos edge.
/// </summary>
public sealed class IntellipayOptions
{
    public const string SectionName = "Intellipay";

    public required string MerchantKey { get; init; }
    public required string ApiKey { get; init; }

    public string BaseUrl { get; init; } = "https://secure.cpteller.com/api/26/webapi.cfc";
    public int HttpTimeoutSeconds { get; init; } = 30;

    /// <summary>TTL de la clave de dedupe emulada en <see cref="BuildingBlocks.Caching.ICacheService"/>
    /// (Intellipay no soporta idempotency-key nativa — ver §44.7.1 del diseño).</summary>
    public TimeSpan IdempotencyCacheTtl { get; init; } = TimeSpan.FromHours(24);
}
