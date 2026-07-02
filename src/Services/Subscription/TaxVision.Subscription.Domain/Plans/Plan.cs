using BuildingBlocks.Domain;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Plan comercial del SaaS. El catálogo se siembra por migración (HasData) con
/// GUID fijos; la gestión dinámica de planes llegará con el panel de plataforma.
/// </summary>
public sealed class Plan : BaseEntity
{
    private Plan() { }

    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public decimal MonthlyPriceUsd { get; private set; }
    public int MaxUsers { get; private set; }
    public int MaxPendingInvitations { get; private set; }
    public long StorageQuotaBytes { get; private set; }
    public string EnabledModulesJson { get; private set; } = "[]";
    public bool IsActive { get; private set; }
    public int SortOrder { get; private set; }

    public static Plan Seed(
        Guid id,
        string code,
        string name,
        string description,
        decimal monthlyPriceUsd,
        int maxUsers,
        int maxPendingInvitations,
        long storageQuotaBytes,
        string enabledModulesJson,
        int sortOrder) =>
        new()
        {
            Id = id,
            Code = code,
            Name = name,
            Description = description,
            MonthlyPriceUsd = monthlyPriceUsd,
            MaxUsers = maxUsers,
            MaxPendingInvitations = maxPendingInvitations,
            StorageQuotaBytes = storageQuotaBytes,
            EnabledModulesJson = enabledModulesJson,
            IsActive = true,
            SortOrder = sortOrder
        };
}

public static class PlanCatalog
{
    public const string Starter = "starter";
    public const string Pro = "pro";
    public const string Enterprise = "enterprise";

    public static readonly Guid StarterId = new("b1000000-0000-0000-0000-000000000001");
    public static readonly Guid ProId = new("b1000000-0000-0000-0000-000000000002");
    public static readonly Guid EnterpriseId = new("b1000000-0000-0000-0000-000000000003");
}
