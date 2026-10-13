namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Base pública con la que PaymentClient compone la URL ESTABLE de una factura. PaymentClient es el
/// dueño de esta URL — conoce el dominio, la ruta y el versionado; Billing solo la guarda y la embebe
/// en el PDF. Con <see cref="UseTenantHosts"/> y un tenant con subdominio, la URL se compone en el host de
/// la oficina con el dominio de PlatformSettings (<c>{Scheme}://{sub}.{dominio}/payments-client/invoices/{ref}</c>)
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

    /// <summary>Componer las URLs públicas en el host de cada oficina (<c>{Scheme}://{sub}.{dominio}</c>, el
    /// mismo host que la firma). El dominio sale de PlatformSettings. Apagado (dev/local) ⇒ se usan las
    /// bases por path de arriba, porque en local <c>{sub}.{dominio}</c> no llega al gateway.</summary>
    public bool UseTenantHosts { get; set; }

    /// <summary>Esquema para los hosts por tenant. Prod = https.</summary>
    public string Scheme { get; set; } = "https";
}
