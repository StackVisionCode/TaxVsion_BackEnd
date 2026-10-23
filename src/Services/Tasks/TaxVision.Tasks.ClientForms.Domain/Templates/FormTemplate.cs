using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Serialization;
using TaxVision.Tasks.ClientForms.Domain.Templates.Events;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Domain.Templates;

public sealed class FormTemplate : AggregateRoot
{
    private FormTemplate() { }

    public FormTemplateName Name { get; private set; } = default!;
    public FormDescription? Description { get; private set; }
    public FormPurpose Purpose { get; private set; }
    public FormTemplateStatus Status { get; private set; }
    public FormDefinitionDocument Draft { get; private set; } = default!;
    public ContentHash DraftHash { get; private set; } = default!;
    public DateTime DraftUpdatedAtUtc { get; private set; }
    public Guid DraftUpdatedByUserId { get; private set; }
    public int LatestVersionNumber { get; private set; }
    public Guid? LatestVersionId { get; private set; }
    public ContentHash? LatestPublishedHash { get; private set; }
    public LibraryOrigin? LibraryOrigin { get; private set; }
    public string? LibraryKey { get; private set; }
    public bool IsLibraryListed { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime? ArchivedAtUtc { get; private set; }
    public Guid? ArchivedByUserId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<FormTemplate> Create(
        Guid tenantId,
        FormTemplateName name,
        FormDescription? description,
        FormDefinitionDocument draft,
        ContentHash draftHash,
        FormPurpose purpose,
        Guid byUserId,
        DateTime nowUtc,
        LibraryOrigin? libraryOrigin = null
    )
    {
        if (tenantId == Guid.Empty || byUserId == Guid.Empty)
            return Result.Failure<FormTemplate>(FormTemplateErrors.InvalidOwner);
        if (!Enum.IsDefined(purpose))
            return Result.Failure<FormTemplate>(FormTemplateErrors.InvalidPurpose);

        var template = new FormTemplate
        {
            Name = name,
            Description = description,
            Purpose = purpose,
            Status = FormTemplateStatus.Active,
            Draft = draft,
            DraftHash = draftHash,
            DraftUpdatedAtUtc = nowUtc,
            DraftUpdatedByUserId = byUserId,
            CreatedAtUtc = nowUtc,
            CreatedByUserId = byUserId,
            LibraryOrigin = libraryOrigin,
        };
        template.SetTenant(tenantId);
        template.AddDomainEvent(new FormTemplateCreated(template.Id, tenantId, byUserId, nowUtc));
        return Result.Success(template);
    }

    public Result Rename(FormTemplateName name, FormDescription? description, Guid byUserId, DateTime nowUtc)
    {
        var active = EnsureActive();
        if (active.IsFailure)
            return active;
        Name = name;
        Description = description;
        AddDomainEvent(new FormTemplateRenamed(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }

    public Result UpdateDraft(
        FormDefinitionDocument draft,
        ContentHash draftHash,
        FormPurpose purpose,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var active = EnsureActive();
        if (active.IsFailure)
            return active;
        Draft = draft;
        DraftHash = draftHash;
        Purpose = purpose;
        DraftUpdatedAtUtc = nowUtc;
        DraftUpdatedByUserId = byUserId;
        AddDomainEvent(new FormTemplateDraftUpdated(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }

    public Result<FormTemplateVersion> Publish(
        CanonicalDefinition canonical,
        string title,
        FormPresentation presentation,
        int componentCount,
        Guid byUserId,
        DateTime nowUtc,
        FormVersionRuleSets? ruleSets = null
    )
    {
        var active = EnsureActive();
        if (active.IsFailure)
            return Result.Failure<FormTemplateVersion>(active.Error);
        if (LatestPublishedHash?.Value == canonical.Sha256)
            return Result.Failure<FormTemplateVersion>(FormTemplateErrors.NothingToPublish);
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200 || componentCount < 0)
            return Result.Failure<FormTemplateVersion>(FormTemplateErrors.InvalidDefinition);

        var hash = ContentHash.Create(canonical.Sha256);
        if (hash.IsFailure)
            return Result.Failure<FormTemplateVersion>(hash.Error);
        var document = FormDefinitionDocument.Create(canonical.Json, FormDefinitionDocument.MaxBytes);
        if (document.IsFailure)
            return Result.Failure<FormTemplateVersion>(document.Error);
        var version = FormTemplateVersion.Create(
            TenantId,
            Id,
            LatestVersionNumber + 1,
            title,
            Purpose,
            presentation,
            document.Value,
            hash.Value,
            componentCount,
            byUserId,
            nowUtc,
            ruleSets
        );
        LatestVersionNumber = version.VersionNumber;
        LatestVersionId = version.Id;
        LatestPublishedHash = hash.Value;
        AddDomainEvent(new FormTemplatePublished(Id, TenantId, version.VersionNumber, byUserId, nowUtc));
        return Result.Success(version);
    }

    public Result Archive(Guid byUserId, DateTime nowUtc)
    {
        var active = EnsureActive();
        if (active.IsFailure)
            return active;
        Status = FormTemplateStatus.Archived;
        ArchivedAtUtc = nowUtc;
        ArchivedByUserId = byUserId;
        AddDomainEvent(new FormTemplateArchived(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }

    public Result Restore(Guid byUserId, DateTime nowUtc)
    {
        if (Status != FormTemplateStatus.Archived)
            return Result.Failure(FormTemplateErrors.NotArchived);
        Status = FormTemplateStatus.Active;
        ArchivedAtUtc = null;
        ArchivedByUserId = null;
        AddDomainEvent(new FormTemplateRestored(Id, TenantId, byUserId, nowUtc));
        return Result.Success();
    }

    public Result RevertDraftTo(FormTemplateVersion version, Guid byUserId, DateTime nowUtc)
    {
        var active = EnsureActive();
        if (active.IsFailure)
            return active;
        if (version.FormTemplateId != Id || version.TenantId != TenantId)
            return Result.Failure(FormTemplateErrors.VersionMismatch);
        Draft = version.Definition;
        DraftHash = version.ContentHash;
        Purpose = version.Purpose;
        DraftUpdatedAtUtc = nowUtc;
        DraftUpdatedByUserId = byUserId;
        AddDomainEvent(new FormTemplateDraftReverted(Id, TenantId, version.VersionNumber, byUserId, nowUtc));
        return Result.Success();
    }

    public Result EnsureDeletable() =>
        LatestVersionNumber > 0 ? Result.Failure(FormTemplateErrors.HasPublishedVersions) : Result.Success();

    private Result EnsureActive() =>
        Status == FormTemplateStatus.Active ? Result.Success() : Result.Failure(FormTemplateErrors.Archived);
}
