namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Etiqueta descriptiva del plan. Plan único D8: ninguna regla la usa; precio, asientos, módulos y
/// claves salen de la edición. Se guarda como texto (<c>HasConversion&lt;string&gt;</c>).
/// </summary>
public enum PlanTier
{
    Starter,
    Pro,
    Enterprise,
    Trial,
}
