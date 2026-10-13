using BuildingBlocks.Platform;

namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Compone las URLs públicas de una factura. Con <see cref="PaymentClientPublicOptions.UseTenantHosts"/>
/// y un tenant con subdominio, usa el host de la oficina con el dominio vigente de la plataforma
/// (<c>{Scheme}://{sub}.{dominio}</c>, el mismo host que la URL de la firma); si no, cae a las bases por
/// path configuradas (dev/local). Fail-safe: un subdominio ausente nunca rompe la URL, solo la deja en
/// la base por path.
/// </summary>
public static class PayablePublicUrls
{
    /// <summary>URL ESTABLE de la factura (la que se embebe en el PDF y vive años).</summary>
    public static string StableInvoiceUrl(
        PaymentClientPublicOptions options,
        PlatformSettingsSnapshot platform,
        string? subDomain,
        string reference
    ) => $"{TenantBaseOrFallback(options, platform, subDomain, options.BaseUrl)}/payments-client/invoices/{reference}";

    /// <summary>URL de la PÁGINA de checkout del frontend a la que redirige el resolver.</summary>
    public static string CheckoutPageUrl(
        PaymentClientPublicOptions options,
        PlatformSettingsSnapshot platform,
        string? subDomain,
        string token
    ) => $"{TenantBaseOrFallback(options, platform, subDomain, options.CheckoutPageBaseUrl)}/pay/{token}";

    /// <summary>
    /// URL a la que se manda al cliente cuando el link NO se pudo resolver (anulado, vencido). No hay
    /// payable del que sacar el tenant, así que el subdominio sale del host por el que entró la
    /// petición: el navegador ya está en el host de la oficina.
    /// </summary>
    public static string UnavailableCheckoutUrl(
        PaymentClientPublicOptions options,
        PlatformSettingsSnapshot platform,
        string? requestHost,
        string reference,
        string reasonCode
    )
    {
        var pageBase = CheckoutPageUrl(options, platform, SubDomainFromHost(options, platform, requestHost), reference);
        return $"{pageBase}?unavailable={Uri.EscapeDataString(reasonCode)}";
    }

    /// <summary>
    /// El subdominio de la oficina sacado del host por el que entró la petición, con la misma regla que
    /// Auth y el Gateway. Null si no se usan hosts por oficina o el host no es de una (en dev es
    /// <c>localhost:5047</c>), y entonces la URL cae a la base configurada.
    /// </summary>
    public static string? SubDomainFromHost(
        PaymentClientPublicOptions options,
        PlatformSettingsSnapshot platform,
        string? host
    ) => options.UseTenantHosts && platform.TryGetTenantSlug(host, out var slug) ? slug : null;

    private static string TenantBaseOrFallback(
        PaymentClientPublicOptions options,
        PlatformSettingsSnapshot platform,
        string? subDomain,
        string fallbackBase
    ) =>
        options.UseTenantHosts && !string.IsNullOrWhiteSpace(subDomain)
            ? $"{options.Scheme}://{platform.TenantHost(subDomain.Trim().ToLowerInvariant())}"
            : fallbackBase.TrimEnd('/');
}
