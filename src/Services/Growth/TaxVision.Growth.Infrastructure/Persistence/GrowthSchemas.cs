namespace TaxVision.Growth.Infrastructure.Persistence;

/// <summary>
/// Physical boundaries inside the single TaxVision_Growth database.
/// Cross-bounded-context foreign keys are intentionally forbidden.
/// </summary>
public static class GrowthSchemas
{
    public const string Codes = "codes";
    public const string Referrals = "referrals";
    public const string Integration = "integration";
    public const string Audit = "audit";

    /// <summary>RBAC Fase 7/8 — proyección local de permisos (UserPermissionsProjection /
    /// RolePermissionsProjection), transversal a Codes y Referrals igual que Audit.</summary>
    public const string Permissions = "permissions";
}
