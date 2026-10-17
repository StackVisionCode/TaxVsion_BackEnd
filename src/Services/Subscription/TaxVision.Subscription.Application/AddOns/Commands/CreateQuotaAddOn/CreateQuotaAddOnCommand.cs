namespace TaxVision.Subscription.Application.AddOns.Commands.CreateQuotaAddOn;

/// <summary>CAT-F3.5 — Admin crea un add-on de cuota genérico: entitlement aditivo sobre una key
/// (<c>storage.max_bytes</c>, <c>invitations.max_pending</c>, …), con estrategia de merge Sum/Max y
/// opcional <see cref="MutexGroup"/> para exclusividad entre SKUs del mismo grupo. Yearly se calcula
/// aparte en el request — no se asume 10×Monthly para no esconder decisiones comerciales.</summary>
public sealed record CreateQuotaAddOnCommand(
    string Code,
    string Name,
    string Description,
    string EntitlementKey,
    string ValueType,
    string EntitlementAmount,
    string MergeStrategy,
    string? MutexGroup,
    decimal MonthlyUsd,
    decimal YearlyUsd,
    Guid ActorUserId
);
