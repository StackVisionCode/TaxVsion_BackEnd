using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Providers;

/// <summary>CRM storage behavior selected for an external provider, independent of quotas and vendor APIs.</summary>
public sealed class TenantStoragePolicy : TenantEntity
{
    private TenantStoragePolicy() { }

    public string ProviderId { get; private set; } = "google-drive";
    public string Mode { get; private set; } = "crm";
    public string? BackupFrequency { get; private set; }
    public string? BackupTime { get; private set; }
    public string? BackupTimeZoneId { get; private set; }
    public DateTime? LastBackupAtUtc { get; private set; }

    public static TenantStoragePolicy Create(Guid tenantId, string providerId)
    {
        var policy = new TenantStoragePolicy { Id = tenantId, ProviderId = providerId };
        policy.SetTenant(tenantId);
        return policy;
    }

    public void Configure(string providerId, string mode, string? frequency, string? time, string? timeZoneId)
    {
        ProviderId = providerId;
        Mode = mode;
        BackupFrequency = mode == "backup" ? frequency : null;
        BackupTime = mode == "backup" ? time : null;
        BackupTimeZoneId = mode == "backup" ? timeZoneId : null;
        if (mode != "backup") LastBackupAtUtc = null;
    }

    public void MarkBackupCompleted(DateTime completedAtUtc) =>
        LastBackupAtUtc = DateTime.SpecifyKind(completedAtUtc, DateTimeKind.Utc);
}
