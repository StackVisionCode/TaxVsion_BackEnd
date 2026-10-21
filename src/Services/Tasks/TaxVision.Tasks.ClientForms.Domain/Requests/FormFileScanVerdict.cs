using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed class FormFileScanVerdict : TenantEntity
{
    private FormFileScanVerdict() { }

    public FormFileVerdictStatus Status { get; private set; }
    public string? OwnerType { get; private set; }
    public Guid? OwnerId { get; private set; }
    public string? FolderType { get; private set; }
    public string? ContentType { get; private set; }
    public long? SizeBytes { get; private set; }
    public string? OriginalName { get; private set; }
    public DateTime LastEventOccurredAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static FormFileScanVerdict Create(
        Guid tenantId,
        Guid fileId,
        FormFileVerdictStatus status,
        string? ownerType,
        Guid? ownerId,
        string? folderType,
        string? contentType,
        long? sizeBytes,
        string? originalName,
        DateTime occurredAtUtc,
        DateTime nowUtc
    )
    {
        var verdict = new FormFileScanVerdict { Id = fileId };
        verdict.SetTenant(tenantId);
        verdict.Apply(status, ownerType, ownerId, folderType, contentType, sizeBytes, originalName, occurredAtUtc, nowUtc);
        return verdict;
    }

    public bool Apply(
        FormFileVerdictStatus status,
        string? ownerType,
        Guid? ownerId,
        string? folderType,
        string? contentType,
        long? sizeBytes,
        string? originalName,
        DateTime occurredAtUtc,
        DateTime nowUtc
    )
    {
        if (occurredAtUtc < LastEventOccurredAtUtc || Status == FormFileVerdictStatus.Deleted)
            return false;
        if (Status is FormFileVerdictStatus.Infected or FormFileVerdictStatus.Blocked or FormFileVerdictStatus.ScanFailed
            && status == FormFileVerdictStatus.Available)
            return false;
        if (Status == status && occurredAtUtc == LastEventOccurredAtUtc)
            return false;

        Status = status;
        OwnerType = ownerType;
        OwnerId = ownerId;
        FolderType = folderType;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        OriginalName = originalName;
        LastEventOccurredAtUtc = occurredAtUtc;
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public FormFileVerdict ToVerdict() => new(Status, OwnerType, OwnerId, FolderType, LastEventOccurredAtUtc);
}
