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
        verdict.Apply(
            status,
            ownerType,
            ownerId,
            folderType,
            contentType,
            sizeBytes,
            originalName,
            occurredAtUtc,
            nowUtc
        );
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
        if (!Accepts(status, occurredAtUtc))
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

    // Los eventos llegan repetidos o desordenados: un negativo no se revierte ni se reemplaza, y
    // Deleted es terminal. Sobre Available solo pasa un re-escaneo negativo más nuevo.
    private bool Accepts(FormFileVerdictStatus incoming, DateTime occurredAtUtc)
    {
        if (occurredAtUtc < LastEventOccurredAtUtc || Status == FormFileVerdictStatus.Deleted)
            return false;
        if (Status == default || incoming == FormFileVerdictStatus.Deleted)
            return true;
        // T-20: la revisión humana se resuelve con un veredicto final posterior (aprobado o bloqueado).
        if (Status == FormFileVerdictStatus.PendingReview)
            return incoming is not FormFileVerdictStatus.PendingReview && occurredAtUtc >= LastEventOccurredAtUtc;
        return Status == FormFileVerdictStatus.Available
            && (
                incoming
                is FormFileVerdictStatus.Infected
                    or FormFileVerdictStatus.Blocked
                    or FormFileVerdictStatus.ScanFailed
            )
            && occurredAtUtc > LastEventOccurredAtUtc;
    }

    public FormFileVerdict ToVerdict() => new(Status, OwnerType, OwnerId, FolderType, LastEventOccurredAtUtc);
}
