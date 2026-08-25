namespace TaxVision.Notification.Application.Common;

/// <summary>URLs públicas del frontend para construir enlaces en los correos.</summary>
public sealed class PortalOptions
{
    public const string SectionName = "Portal";

    /// <summary>Base del CRM (personal de la oficina), p. ej. https://app.taxproffice.com. Los correos
    /// de usuarios de tenant (invitación, reset, etc.) linkean acá.</summary>
    public string BaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Base del PORTAL DEL CLIENTE, p. ej. https://client.taxproffice.com. Los correos del
    /// cliente (pedidos de documentación, doc rechazado) DEBEN linkear acá, NO al CRM — el cliente es
    /// CustomerPortal y no puede entrar a app. Config real por ambiente.
    /// Solo se usa como FALLBACK: con el host del tenant resuelto, el link va a
    /// <c>https://{host}{ClientPathPrefix}</c> (ver <see cref="TenantEmailLinks"/>).</summary>
    public string ClientBaseUrl { get; set; } = "http://localhost:4200";

    /// <summary>Prefijo de ruta del PORTAL DEL CLIENTE dentro del subdominio del tenant, p. ej.
    /// "/portal". Los correos al cliente apuntan a <c>https://{host}{ClientPathPrefix}</c>.</summary>
    public string ClientPathPrefix { get; set; } = "/portal";

    /// <summary>Dominio base usado para enlaces específicos de cada tenant.</summary>
    public string BaseDomain { get; set; } = "taxproffice.com";

    public string ProductName { get; set; } = "TaxProffice";
}
