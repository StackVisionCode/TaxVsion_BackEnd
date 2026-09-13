using TaxVision.PaymentClient.Api.Common;

namespace TaxVision.PaymentClient.Tests.Api;

/// <summary>
/// Verifica la composición de la URL pública de una factura: cuando el tenant tiene subdominio y hay
/// <see cref="PaymentClientPublicOptions.TenantBaseDomain"/>, la URL usa el host de la firma
/// (<c>{sub}.{TenantBaseDomain}</c>); si no, cae a la base por path (dev/local).
/// </summary>
public sealed class PayablePublicUrlsTests
{
    // La referencia real de la factura del reporte del usuario.
    private const string Reference = "OgTkaKWmZubBliu1UYWXapG4FzFE7ezXJDnxNVeqKzo";

    private static PaymentClientPublicOptions ProdLike() =>
        new()
        {
            BaseUrl = "https://pay.taxproffice.com",
            CheckoutPageBaseUrl = "https://app.taxproffice.com",
            TenantBaseDomain = "taxproffice.com",
            Scheme = "https",
        };

    [Fact]
    public void Stable_invoice_url_uses_the_tenant_subdomain_host()
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), subDomain: "castillotax", Reference);

        Assert.Equal($"https://castillotax.taxproffice.com/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Stable_invoice_url_normalizes_subdomain_casing()
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), subDomain: "CastilloTax", Reference);

        Assert.Equal($"https://castillotax.taxproffice.com/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Checkout_page_url_uses_the_tenant_subdomain_host()
    {
        var url = PayablePublicUrls.CheckoutPageUrl(ProdLike(), subDomain: "castillotax", token: "tok_abc123");

        Assert.Equal("https://castillotax.taxproffice.com/pay/tok_abc123", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_path_base_when_subdomain_is_missing(string? subDomain)
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), subDomain, Reference);

        Assert.Equal($"https://pay.taxproffice.com/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Falls_back_to_path_base_when_tenant_base_domain_is_not_configured()
    {
        var options = new PaymentClientPublicOptions
        {
            BaseUrl = "http://localhost:5047",
            TenantBaseDomain = "", // dev/local: sin dominio multi-tenant
        };

        var url = PayablePublicUrls.StableInvoiceUrl(options, subDomain: "castillotax", Reference);

        Assert.Equal($"http://localhost:5047/payments-client/invoices/{Reference}", url);
    }
}
