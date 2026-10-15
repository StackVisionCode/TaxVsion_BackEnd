namespace TaxVision.Subscription.Domain.Billing;

/// <summary>Modo de cobro elegido por el tenant al suscribirse.
/// <c>Automatic</c>: hay método guardado y la plataforma cobra off-session en cada ciclo.
/// <c>Manual</c>: no hay método guardado; el tenant paga cada ciclo tras un aviso.</summary>
public enum BillingMode
{
    Automatic,
    Manual,
}
