namespace TaxVision.PaymentApp.Infrastructure.Providers.PayPal;

/// <summary>SEC-L2 — fallback offline cuando el endpoint <c>v1/notifications/verify-webhook-signature</c>
/// de PayPal cae o devuelve 5xx. Lo decidimos por servicio y no por tenant porque las credenciales son
/// platform-level (ver <see cref="PayPalOptions"/>). Default <c>Enabled=false</c>: en producción se
/// enciende después de que un operador cargue y confirme las URLs reales que PayPal usa en Cert-Url.</summary>
public sealed class PayPalOfflineFallbackOptions
{
    public const string SectionName = "PayPal:OfflineFallback";

    public bool Enabled { get; init; } = false;

    /// <summary>Hosts permitidos para <c>PAYPAL-CERT-URL</c> (defensa anti-SSRF). PayPal firma los webhooks
    /// contra certificados servidos desde sus propios dominios; cualquier otra URL se rechaza antes de
    /// hacer el GET.</summary>
    public IReadOnlyList<string> AllowedCertHosts { get; init; } =
        new[] { "api.paypal.com", "api-m.paypal.com", "api.sandbox.paypal.com", "api-m.sandbox.paypal.com" };

    public TimeSpan CertCacheTtl { get; init; } = TimeSpan.FromHours(24);

    public int HttpTimeoutSeconds { get; init; } = 10;
}
