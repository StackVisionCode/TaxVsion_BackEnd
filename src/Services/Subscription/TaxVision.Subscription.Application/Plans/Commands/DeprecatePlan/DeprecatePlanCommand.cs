namespace TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;

/// <summary>CAT-F2.5 — marca el plan como Deprecated. Un plan deprecated sigue teniendo tenants
/// activos y acepta renovaciones, pero no se ofrece en nuevas contrataciones. Archive es el paso
/// siguiente cuando ya no quedan tenants.</summary>
public sealed record DeprecatePlanCommand(Guid PlanId, Guid ActorUserId);

/// <summary>CAT-F2.5 — archiva un plan previamente deprecated (ningún tenant activo lo tendría ya).</summary>
public sealed record ArchivePlanCommand(Guid PlanId, Guid ActorUserId);
