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

    // ---- SubDomainFromHost: el host de la peticion cuando no hay payable del que sacarlo ----

    [Theory]
    [InlineData("manfer.taxproffice.com", "manfer")]
    [InlineData("MANFER.TaxProffice.com", "manfer")]
    [InlineData("manfer.taxproffice.com:443", "manfer")]
    public void The_office_subdomain_comes_from_the_request_host(string host, string expected) =>
        Assert.Equal(expected, PayablePublicUrls.SubDomainFromHost(ProdLike(), host));

    [Theory]
    [InlineData("localhost:5047")] // dev: cae a la base configurada, como antes
    [InlineData("taxproffice.com")] // el apex no es una oficina
    [InlineData("a.b.taxproffice.com")] // dos niveles: no se sabe cual es la oficina
    [InlineData("manfer.otrodominio.com")] // otro dominio: no es nuestro
    [InlineData("")]
    [InlineData(null)]
    public void A_host_that_is_not_an_office_gives_null(string? host) =>
        Assert.Null(PayablePublicUrls.SubDomainFromHost(ProdLike(), host));

    [Fact]
    public void Without_TenantBaseDomain_nothing_is_derived()
    {
        var options = ProdLike();
        options.TenantBaseDomain = "";

        Assert.Null(PayablePublicUrls.SubDomainFromHost(options, "manfer.taxproffice.com"));
    }

    [Fact]
    public void A_revoked_link_keeps_the_client_on_the_office_host()
    {
        // El bug reportado: el resolver pasaba subDomain null fijo, asi que un link anulado mandaba al
        // cliente a la base por path — en produccion, localhost:4200.
        var url = PayablePublicUrls.UnavailableCheckoutUrl(
            ProdLike(),
            "manfer.taxproffice.com",
            Reference,
            "Payable.Revoked"
        );

        Assert.Equal($"https://manfer.taxproffice.com/pay/{Reference}?unavailable=Payable.Revoked", url);
        Assert.DoesNotContain("localhost", url, StringComparison.Ordinal);
    }

    [Fact]
    public void In_dev_the_unavailable_url_still_falls_back_to_the_configured_base()
    {
        var url = PayablePublicUrls.UnavailableCheckoutUrl(ProdLike(), "localhost:5047", Reference, "Payable.Expired");

        Assert.Equal($"https://app.taxproffice.com/pay/{Reference}?unavailable=Payable.Expired", url);
    }

    [Fact]
    public void The_reason_code_is_escaped()
    {
        var url = PayablePublicUrls.UnavailableCheckoutUrl(ProdLike(), "manfer.taxproffice.com", Reference, "a b&c");

        Assert.EndsWith("?unavailable=a%20b%26c", url, StringComparison.Ordinal);
    }
}
