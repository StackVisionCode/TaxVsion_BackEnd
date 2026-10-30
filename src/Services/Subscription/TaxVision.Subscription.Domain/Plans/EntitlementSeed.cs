using TaxVision.Subscription.Domain.Entitlements;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Clave de entitlement con su valor por defecto, lista para sembrarse en una versión de plan
/// (ver <see cref="SubscriptionPlan.AddMissingEntitlements"/>). El valor viaja como string porque
/// así lo persiste <see cref="PlanEntitlementDefinition"/> y así llega al snapshot del tenant.
/// </summary>
public sealed record EntitlementSeed(string Key, EntitlementValueType ValueType, string DefaultValue);
