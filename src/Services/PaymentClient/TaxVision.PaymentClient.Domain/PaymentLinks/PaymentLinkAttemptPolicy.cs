namespace TaxVision.PaymentClient.Domain.PaymentLinks;

/// <summary>
/// Límite de intentos de canje fallidos por link (§41.4/K.1) — el token del link es la única
/// prueba de posesión, así que sin este límite alguien con el link podría probar tarjetas
/// robadas contra el monto fijado hasta que una funcione.
/// </summary>
public static class PaymentLinkAttemptPolicy
{
    public const int MaxRedemptionAttemptsPerLink = 5;
}
