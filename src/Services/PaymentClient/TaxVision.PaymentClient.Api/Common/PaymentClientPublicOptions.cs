namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Base pública con la que PaymentClient compone la URL ESTABLE de una factura. PaymentClient es el
/// dueño de esta URL — conoce el dominio, la ruta y el versionado; Billing solo la guarda y la embebe
/// en el PDF. Cuando <see cref="TenantBaseDomain"/> está seteado y el tenant tiene subdominio, la URL
/// se compone en el subdominio del tenant (<c>{Scheme}://{sub}.{TenantBaseDomain}/payments-client/invoices/{ref}</c>)
/// — el mismo host que usa la firma; si no, cae a la base por path (<see cref="BaseUrl"/>), para dev/local.
/// La composición vive en <see cref="PayablePublicUrls"/>.
/// </summary>
public sealed class PaymentClientPublicOptions
{
    public const string SectionName = "PaymentClient:Public";

    /// <summary>Base por PATH (fallback) para la URL ESTABLE de facturas cuando no hay subdominio de
    /// tenant disponible (<c>{BaseUrl}/payments-client/invoices/{ref}</c>). Dev/local.</summary>
    public string BaseUrl { get; set; } = "http://localhost:5047";

    /// <summary>Base por PATH (fallback) de la PÁGINA de checkout del frontend a la que redirige el
    /// resolver (<c>{CheckoutPageBaseUrl}/pay/{token}</c>). Dev = ng serve (4200).</summary>
    public string CheckoutPageBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Dominio base multi-tenant (p.ej. <c>taxproffice.com</c>). Cuando está seteado y el
    /// tenant tiene subdominio, las URLs públicas se componen en el subdominio del tenant
    /// (<c>{Scheme}://{sub}.{TenantBaseDomain}</c>) — el mismo host que la firma. Vacío ⇒ se usan las
    /// bases por path de arriba.</summary>
    public string TenantBaseDomain { get; set; } = "";

    /// <summary>Esquema para los hosts por tenant. Prod = https.</summary>
    public string Scheme { get; set; } = "https";
}
