namespace TaxVision.Growth.Api.RateLimiting;

/// <summary>
/// Nombres de las políticas de rate limiting registradas en <c>Program.cs</c> y aplicadas en los
/// controllers vía <c>[EnableRateLimiting]</c>. Centralizados para evitar drift entre el registro
/// y el uso. Cierra el gap B-02 (endpoints públicos/M2M sin tope → enumeración/brute-force de
/// códigos): el Gateway solo limita <c>/auth/*</c> y <c>/storage/*</c>, así que <c>/growth/*</c>
/// necesita su propio limitador. Ver <c>20_Growth_Security_Model.md</c>.
/// </summary>
public static class GrowthRateLimitPolicies
{
    /// <summary>Ingreso self-service de atribución de referido. Particiona por tenant (claim del
    /// JWT) con fallback a IP, para que un tenant no pueda sondear el espacio de ReferralCode
    /// aprovechando el oráculo Invalid-vs-NotFound.</summary>
    public const string ReferralAttribution = "growth-referral-attribution";

    /// <summary>Creación de quotes (M2M desde Payment). Particiona por IP del servicio llamador
    /// con un tope alto: acota sondeo/carga abusiva sin frenar el flujo de checkout legítimo.</summary>
    public const string CodeQuote = "growth-code-quote";
}
