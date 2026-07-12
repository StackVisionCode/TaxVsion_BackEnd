using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Entitlements;

/// <summary>
/// Un valor de entitlement resuelto para un tenant en un momento dado. Inmutable — el
/// snapshot se reemplaza entero en cada recálculo, nunca se editan entries sueltas.
/// </summary>
public sealed record EntitlementEntry(
    EntitlementKey Key,
    EntitlementValueType ValueType,
    string Value,
    EntitlementStatus Status,
    EntitlementSource Source,
    DateTime? ExpiresAtUtc
);
