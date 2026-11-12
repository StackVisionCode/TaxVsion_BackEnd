namespace TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;

/// <summary>
/// Autoría de planes: fija el conjunto de módulos (<c>module.*</c>) de un plan. Publica una versión
/// nueva (la publicada es inmutable) y dispara el recálculo masivo de sus tenants.
/// </summary>
/// <param name="SystemActor">Proceso del sistema que lo pide (por ejemplo el reconciliador), para la auditoría.</param>
public sealed record SetPlanModulesCommand(
    Guid PlanId,
    IReadOnlyList<string> Modules,
    Guid ActorUserId,
    string? SystemActor = null
);
