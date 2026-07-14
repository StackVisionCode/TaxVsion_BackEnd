namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>Cómo se combina el valor de un entitlement de add-on con el valor heredado
/// del plan al construir el TenantEntitlementSnapshot.</summary>
public enum AddOnMergeStrategy
{
    Or,
    Max,
    Sum,
    Replace,
}
