namespace TaxVision.PaymentClient.Domain.Connect;

/// <summary>Tipo de Connected Account de Stripe — determina quién ve el dashboard de Stripe
/// (Standard: el tenant; Express: versión simplificada co-branded; Custom: invisible, la
/// plataforma controla toda la UI).</summary>
public enum ConnectAccountType
{
    Standard = 1,
    Express = 2,
    Custom = 3,
}
