namespace TaxVision.PaymentClient.Domain.TenantPaymentConfigs;

/// <summary>
/// Los dos modelos de cobro que PaymentClient soporta simultáneamente (§19 del diseño — el
/// documento deliberadamente no elige uno):
/// <list type="bullet">
/// <item><see cref="DirectApiKeys"/>: el tenant trae su propia cuenta Stripe (secret key +
/// webhook secret cifrados en <c>TenantPaymentConfig</c>); los fondos van directo a esa
/// cuenta, la plataforma cobra su fee fuera de banda (vía PaymentApp).</item>
/// <item><see cref="Connect"/>: la plataforma crea una Connected Account (Stripe Connect) y
/// cobra en nombre del tenant vía <c>TenantConnectAccount</c>, reteniendo un
/// <c>application_fee_amount</c> nativo por cada cobro.</item>
/// </list>
/// </summary>
public enum TenantPaymentMode
{
    DirectApiKeys = 1,
    Connect = 2,
}
