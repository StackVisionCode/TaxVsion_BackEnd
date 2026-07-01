namespace TaxVision.Subscription.Domain.Modules;

/// <summary>
/// Join entity for the N:M relationship between Plan and Module.
/// A Plan can include many Modules; a Module can belong to many Plans.
/// </summary>
public sealed class PlanModule
{
    public Guid PlanId { get; private set; }
    public Guid ModuleId { get; private set; }

    // Navigation
    public Module? Module { get; private set; }

    private PlanModule() { }

    public static PlanModule Create(Guid planId, Guid moduleId)
        => new() { PlanId = planId, ModuleId = moduleId };
}
