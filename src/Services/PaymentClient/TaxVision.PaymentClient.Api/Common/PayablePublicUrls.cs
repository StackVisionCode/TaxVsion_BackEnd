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

    /// <summary>
    /// URL a la que se manda al cliente cuando el link NO se pudo resolver (anulado, vencido). No hay
    /// payable del que sacar el tenant, así que el subdominio sale del host por el que entró la
    /// petición: el navegador ya está en el host de la oficina.
    /// </summary>
    public static string UnavailableCheckoutUrl(
        PaymentClientPublicOptions options,
        string? requestHost,
        string reference,
        string reasonCode
    )
    {
        var pageBase = CheckoutPageUrl(options, SubDomainFromHost(options, requestHost), reference);
        return $"{pageBase}?unavailable={Uri.EscapeDataString(reasonCode)}";
    }

    /// <summary>
    /// El subdominio de la oficina sacado del host por el que entró la petición. Null si el host no
    /// cuelga del dominio multi-tenant — en dev es <c>localhost:5047</c>, y entonces la URL cae a la
    /// base configurada.
    /// </summary>
    public static string? SubDomainFromHost(PaymentClientPublicOptions options, string? host)
    {
        var baseDomain = options.TenantBaseDomain?.Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(baseDomain))
            return null;

        // Sin puerto: el host de tenant es público y va por 443.
        var name = host.Split(':')[0].Trim().TrimEnd('.').ToLowerInvariant();
        var suffix = $".{baseDomain.ToLowerInvariant()}";
        if (!name.EndsWith(suffix, StringComparison.Ordinal))
            return null;

        var subDomain = name[..^suffix.Length];
        // Un solo nivel: "manfer" sí, "a.b" no (no sabríamos cuál es la oficina).
        return subDomain.Length > 0 && !subDomain.Contains('.') ? subDomain : null;
    }

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
