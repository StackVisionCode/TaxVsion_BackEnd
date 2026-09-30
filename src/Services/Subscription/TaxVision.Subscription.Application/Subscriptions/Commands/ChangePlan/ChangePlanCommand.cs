namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary>
/// <paramref name="BillingCycle"/> es opcional — "Monthly"/"Yearly"/etc (string, se parsea en el handler).
/// Null = mantener el ciclo actual de la suscripción.
/// <para>
/// Cómo se cobra un upgrade lo decide el caller: con <paramref name="SuccessUrl"/> y
/// <paramref name="CancelUrl"/> se cobra por CHECKOUT hosteado (el único camino para un tenant sin método en
/// archivo); sin ellas, off-session contra el método guardado, como siempre.
/// </para>
/// </summary>
public sealed record ChangePlanCommand(
    Guid TenantId,
    string PlanCode,
    string? BillingCycle,
    Guid RequestedByUserId,
    string? PayerEmail = null,
    string? SuccessUrl = null,
    string? CancelUrl = null
)
{
    /// <summary>El checkout necesita a quién facturar y a dónde volver; si falta algo, va off-session.</summary>
    public bool WantsHostedCheckout =>
        !string.IsNullOrWhiteSpace(PayerEmail)
        && !string.IsNullOrWhiteSpace(SuccessUrl)
        && !string.IsNullOrWhiteSpace(CancelUrl);
}
