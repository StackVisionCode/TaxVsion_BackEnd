using BuildingBlocks.Platform;
using TaxVision.PaymentClient.Api.Common;

namespace TaxVision.PaymentClient.Tests.Api;

/// <summary>
/// Verifica la composición de la URL pública de una factura: con
/// <see cref="PaymentClientPublicOptions.UseTenantHosts"/> y un tenant con subdominio, la URL usa el host
/// de la firma (<c>{sub}.{dominio de PlatformSettings}</c>); si no, cae a la base por path (dev/local).
/// </summary>
public sealed class PayablePublicUrlsTests
{
    // La referencia real de la factura del reporte del usuario.
    private const string Reference = "OgTkaKWmZubBliu1UYWXapG4FzFE7ezXJDnxNVeqKzo";

    private static readonly PlatformSettingsSnapshot Platform = new(
        1,
        "TaxOffix",
        "TaxOffix LLC",
        "taxoffix.io",
        "api.taxoffix.io",
        "support@taxoffix.io",
        "billing@taxoffix.io"
    );

    private static PaymentClientPublicOptions ProdLike() =>
        new()
        {
            BaseUrl = "https://api.taxoffix.io",
            CheckoutPageBaseUrl = "https://app.taxoffix.io",
            UseTenantHosts = true,
            Scheme = "https",
        };

    [Fact]
    public void Stable_invoice_url_uses_the_tenant_subdomain_host()
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), Platform, subDomain: "castillotax", Reference);

        Assert.Equal($"https://castillotax.taxoffix.io/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Stable_invoice_url_normalizes_subdomain_casing()
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), Platform, subDomain: "CastilloTax", Reference);

        Assert.Equal($"https://castillotax.taxoffix.io/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Checkout_page_url_uses_the_tenant_subdomain_host()
    {
        var url = PayablePublicUrls.CheckoutPageUrl(
            ProdLike(),
            Platform,
            subDomain: "castillotax",
            token: "tok_abc123"
        );

        Assert.Equal("https://castillotax.taxoffix.io/pay/tok_abc123", url);
    }

    [Fact]
    public void The_tenant_host_follows_the_current_platform_domain()
    {
        var moved = Platform with { BaseDomain = "taxoffix.com" };

        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), moved, subDomain: "castillotax", Reference);

        Assert.Equal($"https://castillotax.taxoffix.com/payments-client/invoices/{Reference}", url);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Falls_back_to_path_base_when_subdomain_is_missing(string? subDomain)
    {
        var url = PayablePublicUrls.StableInvoiceUrl(ProdLike(), Platform, subDomain, Reference);

        Assert.Equal($"https://api.taxoffix.io/payments-client/invoices/{Reference}", url);
    }

    [Fact]
    public void Falls_back_to_path_base_when_tenant_hosts_are_off()
    {
        var options = new PaymentClientPublicOptions
        {
            BaseUrl = "http://localhost:5047",
            UseTenantHosts = false, // dev/local: {sub}.<dominio> no llega al gateway
        };

        var url = PayablePublicUrls.StableInvoiceUrl(options, Platform, subDomain: "castillotax", Reference);

        Assert.Equal($"http://localhost:5047/payments-client/invoices/{Reference}", url);
    }

    // ---- SubDomainFromHost: el host de la peticion cuando no hay payable del que sacarlo ----

    [Theory]
    [InlineData("manfer.taxoffix.io", "manfer")]
    [InlineData("MANFER.TaxOffix.io", "manfer")]
    [InlineData("manfer.taxoffix.io:443", "manfer")]
    public void The_office_subdomain_comes_from_the_request_host(string host, string expected) =>
        Assert.Equal(expected, PayablePublicUrls.SubDomainFromHost(ProdLike(), Platform, host));

    [Theory]
    [InlineData("localhost:5047")] // dev: cae a la base configurada, como antes
    [InlineData("taxoffix.io")] // el apex no es una oficina
    [InlineData("app.taxoffix.io")] // subdominio de la plataforma, no de una oficina
    [InlineData("a.b.taxoffix.io")] // dos niveles: no se sabe cual es la oficina
    [InlineData("manfer.otrodominio.com")] // otro dominio: no es nuestro
    [InlineData("")]
    [InlineData(null)]
    public void A_host_that_is_not_an_office_gives_null(string? host) =>
        Assert.Null(PayablePublicUrls.SubDomainFromHost(ProdLike(), Platform, host));

    [Fact]
    public void Without_tenant_hosts_nothing_is_derived()
    {
        var options = ProdLike();
        options.UseTenantHosts = false;

        Assert.Null(PayablePublicUrls.SubDomainFromHost(options, Platform, "manfer.taxoffix.io"));
    }

    [Fact]
    public void A_revoked_link_keeps_the_client_on_the_office_host()
    {
        // El bug reportado: el resolver pasaba subDomain null fijo, asi que un link anulado mandaba al
        // cliente a la base por path — en produccion, localhost:4200.
        var url = PayablePublicUrls.UnavailableCheckoutUrl(
            ProdLike(),
            Platform,
            "manfer.taxoffix.io",
            Reference,
            "Payable.Revoked"
        );

        Assert.Equal($"https://manfer.taxoffix.io/pay/{Reference}?unavailable=Payable.Revoked", url);
        Assert.DoesNotContain("localhost", url, StringComparison.Ordinal);
    }

    [Fact]
    public void In_dev_the_unavailable_url_still_falls_back_to_the_configured_base()
    {
        var url = PayablePublicUrls.UnavailableCheckoutUrl(
            ProdLike(),
            Platform,
            "localhost:5047",
            Reference,
            "Payable.Expired"
        );

        Assert.Equal($"https://app.taxoffix.io/pay/{Reference}?unavailable=Payable.Expired", url);
    }

    [Fact]
    public void The_reason_code_is_escaped()
    {
        var url = PayablePublicUrls.UnavailableCheckoutUrl(
            ProdLike(),
            Platform,
            "manfer.taxoffix.io",
            Reference,
            "a b&c"
        );

        Assert.EndsWith("?unavailable=a%20b%26c", url, StringComparison.Ordinal);
    }
}
