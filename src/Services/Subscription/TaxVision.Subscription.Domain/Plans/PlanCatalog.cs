namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Códigos y GUIDs deterministas del catálogo inicial de planes, sembrado por migración
/// (HasData) en TaxVision.Subscription.Infrastructure. La gestión dinámica de planes
/// llegará con el panel de plataforma (fase administrativa).
/// </summary>
public static class PlanCatalog
{
    public const string Starter = "starter";
    public const string Pro = "pro";
    public const string Enterprise = "enterprise";

    public static readonly Guid StarterId = new("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid ProId = new("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid EnterpriseId = new("b1000000-0000-0000-0000-000000000003");

    public static readonly Guid StarterV1Id = new("b2000000-0000-0000-0000-000000000001");
    public static readonly Guid ProV1Id = new("b2000000-0000-0000-0000-000000000002");
    public static readonly Guid EnterpriseV1Id = new("b2000000-0000-0000-0000-000000000003");
}
