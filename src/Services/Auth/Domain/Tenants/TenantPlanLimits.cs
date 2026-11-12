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

    /// <summary>Última revisión de entitlements aplicada; una menor llega tarde y se ignora.</summary>
    public long? LastEntitlementsRevision { get; private set; }

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

    /// <summary>
    /// Aplica los límites de una revisión de entitlements. Una revisión menor que la ya aplicada llega tarde y se
    /// ignora (devuelve false): sin esto, un evento viejo bajaba usuarios o almacenamiento a un valor anterior.
    /// La misma revisión se vuelve a aplicar (idempotente).
    /// </summary>
    public bool ApplyRevision(
        long revision,
        string planCode,
        int maxUsers,
        int maxPendingInvitations,
        long storageQuotaBytes,
        string enabledModulesJson,
        bool suspendedForBilling
    )
    {
        if (LastEntitlementsRevision is { } applied && revision < applied)
            return false;

        Apply(planCode, maxUsers, maxPendingInvitations, storageQuotaBytes, enabledModulesJson);
        SetSuspendedForBilling(suspendedForBilling);
        LastEntitlementsRevision = revision;
        return true;
    }

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
