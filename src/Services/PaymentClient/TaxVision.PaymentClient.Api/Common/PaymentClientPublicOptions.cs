namespace TaxVision.PaymentClient.Api.Common;

/// <summary>
/// Base pública con la que PaymentClient compone la URL ESTABLE de una factura
/// (<c>{BaseUrl}/payments-client/invoices/{reference}</c>). PaymentClient es el dueño de esta URL —
/// conoce el dominio, la ruta y el versionado; Billing solo la guarda y la embebe en el PDF. El
/// subdominio por tenant (<c>{sub}.pay.taxvision.app</c>) es un refinamiento posterior (requiere
/// lookup del subdominio); por ahora la base es por path y configurable.
/// </summary>
public sealed class PaymentClientPublicOptions
{
    public const string SectionName = "PaymentClient:Public";

    /// <summary>Base con la que se compone la URL ESTABLE de facturas (<c>{BaseUrl}/payments-client/invoices/{ref}</c>).</summary>
    public string BaseUrl { get; set; } = "http://localhost:5047";

    /// <summary>Base de la PÁGINA de checkout del frontend a la que redirige el resolver
    /// (<c>{CheckoutPageBaseUrl}/pay/{token}</c>). Dev = ng serve (4200).</summary>
    public string CheckoutPageBaseUrl { get; set; } = "http://localhost:4200";
}
