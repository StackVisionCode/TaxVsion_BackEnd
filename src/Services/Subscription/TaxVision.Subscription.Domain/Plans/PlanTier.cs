namespace TaxVision.Subscription.Domain.Plans;

/// <summary>CAT-F5.3 — Renombrado <c>Standard</c> → <c>Starter</c> para alinear con el naming
/// canónico del ADR y con <see cref="TaxVision.Auth.Domain.Tenants.PlanTier"/>. La columna
/// <c>Plans.Tier</c> es <c>HasConversion&lt;string&gt;</c>, así que requirió migración de datos
/// (<c>UPDATE SubscriptionPlans SET Tier='Starter' WHERE Tier='Standard'</c>). Pre-check verificó 0
/// filas afectadas en local (DB sin planes sembrados en ese momento).</summary>
public enum PlanTier
{
    Starter,
    Pro,
    Enterprise,
    Trial,
}
