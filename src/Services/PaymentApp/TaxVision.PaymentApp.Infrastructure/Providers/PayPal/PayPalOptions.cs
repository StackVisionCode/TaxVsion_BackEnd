namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>
/// Platform-level PayPal REST configuration. Credentials are environment secrets, not tenant data.
/// Sandbox default is intentional so local/dev cannot accidentally hit live PayPal.
/// </summary>
public sealed class PayPalOptions
{
    public const string SectionName = "PayPal";

    public string BaseUrl { get; init; } = "https://api-m.sandbox.paypal.com";
    public string ClientId { get; init; } = string.Empty;
    public string ClientSecret { get; init; } = string.Empty;
    public string? WebhookId { get; init; }
    public int HttpTimeoutSeconds { get; init; } = 30;
    public TimeSpan AccessTokenCacheTtl { get; init; } = TimeSpan.FromMinutes(45);
}
