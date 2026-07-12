using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Tenants;

/// <summary>
/// Proyección local de los límites del plan contratado, alimentada por eventos del
/// servicio Subscription. Id = TenantId.
/// </summary>
public sealed class TenantPlanLimits : BaseEntity
{
    private TenantPlanLimits() { }

    public string PlanCode { get; private set; } = default!;
    public int MaxUsers { get; private set; }
    public int MaxPendingInvitations { get; private set; }
    public long StorageQuotaBytes { get; private set; }
    public string EnabledModulesJson { get; private set; } = "[]";
    public bool IsSuspendedForBilling { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantPlanLimits Create(
        Guid tenantId,
        string planCode,
        int maxUsers,
        int maxPendingInvitations,
        long storageQuotaBytes,
        string enabledModulesJson
    ) =>
        new()
        {
            Id = tenantId,
            PlanCode = planCode,
            MaxUsers = maxUsers,
            MaxPendingInvitations = maxPendingInvitations,
            StorageQuotaBytes = storageQuotaBytes,
            EnabledModulesJson = enabledModulesJson,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    public void Apply(
        string planCode,
        int maxUsers,
        int maxPendingInvitations,
        long storageQuotaBytes,
        string enabledModulesJson
    )
    {
        PlanCode = planCode;
        MaxUsers = maxUsers;
        MaxPendingInvitations = maxPendingInvitations;
        StorageQuotaBytes = storageQuotaBytes;
        EnabledModulesJson = enabledModulesJson;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetMaxUsers(int maxUsers)
    {
        MaxUsers = maxUsers;
        UpdatedAtUtc = DateTime.UtcNow;
    }

    public void SetSuspendedForBilling(bool suspended)
    {
        IsSuspendedForBilling = suspended;
        UpdatedAtUtc = DateTime.UtcNow;
    }
}
