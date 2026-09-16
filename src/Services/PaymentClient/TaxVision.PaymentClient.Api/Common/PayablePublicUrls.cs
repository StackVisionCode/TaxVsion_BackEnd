namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Compone las URLs públicas de una factura. Si el tenant tiene subdominio y hay
/// <see cref="PaymentClientPublicOptions.TenantBaseDomain"/> configurado, usa el host por tenant
/// (<c>{Scheme}://{sub}.{TenantBaseDomain}</c>) — el mismo host que la URL de la firma; si no, cae a
/// las bases por path configuradas (dev/local). Fail-safe: un subdominio ausente nunca rompe la URL,
/// solo la deja en la base por path.
/// </summary>
public static class PayablePublicUrls
{
    /// <summary>URL ESTABLE de la factura (la que se embebe en el PDF y vive años).</summary>
    public static string StableInvoiceUrl(PaymentClientPublicOptions options, string? subDomain, string reference) =>
        $"{TenantBaseOrFallback(options, subDomain, options.BaseUrl)}/payments-client/invoices/{reference}";

    /// <summary>URL de la PÁGINA de checkout del frontend a la que redirige el resolver.</summary>
    public static string CheckoutPageUrl(PaymentClientPublicOptions options, string? subDomain, string token) =>
        $"{TenantBaseOrFallback(options, subDomain, options.CheckoutPageBaseUrl)}/pay/{token}";

    private static string TenantBaseOrFallback(
        PaymentClientPublicOptions options,
        string? subDomain,
        string fallbackBase
    )
    {
        if (!string.IsNullOrWhiteSpace(subDomain) && !string.IsNullOrWhiteSpace(options.TenantBaseDomain))
        {
            var host = $"{subDomain.Trim().ToLowerInvariant()}.{options.TenantBaseDomain.Trim().Trim('.')}";
            return $"{options.Scheme}://{host}";
        }

        return fallbackBase.TrimEnd('/');
    }
}
