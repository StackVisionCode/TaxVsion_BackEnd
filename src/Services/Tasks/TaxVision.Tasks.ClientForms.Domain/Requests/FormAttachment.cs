using BuildingBlocks.Domain;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed class FormAttachment : TenantEntity
{
    private FormAttachment() { }

    public Guid FormSubmissionId { get; private set; }
    public string ComponentId { get; private set; } = string.Empty;
    public string? RowId { get; private set; }
    public Guid FileId { get; private set; }
    public string DisplayName { get; private set; } = string.Empty;
    public string ContentType { get; private set; } = string.Empty;
    public long SizeBytes { get; private set; }
    public FormAttachmentStatus Status { get; private set; }
    public string? RejectionCode { get; private set; }
    public DateTime RegisteredAtUtc { get; private set; }
    public DateTime? ResolvedAtUtc { get; private set; }
    public DateTime? DetachedAtUtc { get; private set; }
    public string? OwnerReassignment { get; private set; }
    public DateTime? OwnerReassignedAtUtc { get; private set; }
    public FormAnswerAddress Address => new(ComponentId, RowId);

    // Ya es del cliente: no hace falta volver a pedir la reasignación.
    public bool OwnerReassignmentSettled =>
        OwnerReassignment is FormFileReassignmentOutcomes.Reassigned or FormFileReassignmentOutcomes.AlreadyApplied;

    internal static FormAttachment Create(
        Guid tenantId,
        Guid submissionId,
        FormAnswerAddress address,
        Guid fileId,
        string displayName,
        string contentType,
        long sizeBytes,
        DateTime nowUtc
    )
    {
        var attachment = new FormAttachment
        {
            FormSubmissionId = submissionId,
            ComponentId = address.ComponentId,
            RowId = address.RowId,
            FileId = fileId,
            DisplayName = displayName.Trim(),
            ContentType = contentType.Trim(),
            SizeBytes = sizeBytes,
            Status = FormAttachmentStatus.Pending,
            RegisteredAtUtc = nowUtc,
        };
        attachment.SetTenant(tenantId);
        return attachment;
    }

    internal FileVerdictOutcome ApplyVerdict(
        FormFileVerdict verdict,
        IReadOnlyCollection<(string OwnerType, Guid OwnerId)> expectedOwners,
        DateTime nowUtc
    )
    {
        if (Status == FormAttachmentStatus.Detached)
            return FileVerdictOutcome.Ignored;
        if (verdict.Status == FormFileVerdictStatus.Deleted)
        {
            Detach(nowUtc);
            return FileVerdictOutcome.Detached;
        }
        if (Status == FormAttachmentStatus.Available)
            return FileVerdictOutcome.Ignored;

        var rejection = verdict.Status switch
        {
            FormFileVerdictStatus.Infected => "infected",
            FormFileVerdictStatus.Blocked => "blocked_by_policy",
            FormFileVerdictStatus.ScanFailed => "scan_failed",
            FormFileVerdictStatus.Available
                when !string.Equals(verdict.FolderType, "ClientForms", StringComparison.Ordinal) => "wrong_folder",
            FormFileVerdictStatus.Available
                when verdict.OwnerId is null
                    || string.IsNullOrWhiteSpace(verdict.OwnerType)
                    || !IsExpectedOwner(expectedOwners, verdict.OwnerType, verdict.OwnerId.Value) => "not_owned",
            _ => null,
        };

        ResolvedAtUtc = nowUtc;
        if (rejection is null && verdict.Status == FormFileVerdictStatus.Available)
        {
            Status = FormAttachmentStatus.Available;
            RejectionCode = null;
            return FileVerdictOutcome.Available;
        }

        Status = FormAttachmentStatus.Rejected;
        RejectionCode = rejection ?? "scan_failed";
        return FileVerdictOutcome.Rejected;
    }

    internal void Expire(DateTime nowUtc)
    {
        if (Status != FormAttachmentStatus.Pending)
            return;
        Status = FormAttachmentStatus.Rejected;
        RejectionCode = "upload_incomplete";
        ResolvedAtUtc = nowUtc;
    }

    internal void RecordOwnerReassignment(string outcome, DateTime nowUtc)
    {
        OwnerReassignment = outcome;
        OwnerReassignedAtUtc = nowUtc;
    }

    internal void Detach(DateTime nowUtc)
    {
        Status = FormAttachmentStatus.Detached;
        DetachedAtUtc = nowUtc;
    }

    private static bool IsExpectedOwner(
        IReadOnlyCollection<(string OwnerType, Guid OwnerId)> expectedOwners,
        string ownerType,
        Guid ownerId
    )
    {
        foreach (var owner in expectedOwners)
        {
            if (string.Equals(owner.OwnerType, ownerType, StringComparison.Ordinal) && owner.OwnerId == ownerId)
                return true;
        }
        return false;
    }
}
