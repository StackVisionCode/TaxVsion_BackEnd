using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Domain.Layouts;

/// <summary>
/// Layout base de correo (System o Tenant) — análogo a <see cref="Templates.EmailTemplate"/>. Todo
/// EmailTemplateVersion debe extender una versión Published de un layout (§14.7 del plan): no se
/// permite un template standalone con HTML completo propio.
/// </summary>
public sealed class EmailLayout : BaseEntity
{
    private readonly List<EmailLayoutVersion> _versions = [];

    private EmailLayout() { }

    public Guid? TenantId { get; private set; }
    public TemplateScope Scope { get; private set; }
    public LayoutKey LayoutKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public EmailContentStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<EmailLayoutVersion> Versions => _versions.AsReadOnly();

    public static Result<EmailLayout> CreateNew(
        TemplateScope scope,
        Guid? tenantId,
        LayoutKey layoutKey,
        string name,
        string? description,
        Guid createdByUserId,
        DateTime createdAtUtc
    )
    {
        if (scope == TemplateScope.Tenant && tenantId is null)
            return Result.Failure<EmailLayout>(
                new Error("EmailLayout.TenantRequired", "TenantId is required for Tenant-scoped layouts.")
            );

        if (scope == TemplateScope.System && tenantId is not null)
            return Result.Failure<EmailLayout>(
                new Error("EmailLayout.TenantNotAllowed", "TenantId must be null for System-scoped layouts.")
            );

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result.Failure<EmailLayout>(
                new Error("EmailLayout.Name", "Name is required and must be at most 200 characters.")
            );

        return Result.Success(
            new EmailLayout
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Scope = scope,
                LayoutKey = layoutKey,
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Status = EmailContentStatus.Active,
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = createdAtUtc,
            }
        );
    }

    public Result<EmailLayoutVersion> AddDraftVersion(
        string htmlStorageKey,
        Guid htmlFileId,
        string? designJsonStorageKey,
        Guid? designJsonFileId,
        string? previewImageStorageKey,
        Guid? previewImageFileId,
        DateTime nowUtc
    )
    {
        if (Status != EmailContentStatus.Active)
            return Result.Failure<EmailLayoutVersion>(
                new Error("EmailLayout.NotActive", "Cannot add a version to a non-active layout.")
            );

        var nextVersionNumber = 1;
        foreach (var version in _versions)
            if (version.VersionNumber >= nextVersionNumber)
                nextVersionNumber = version.VersionNumber + 1;

        var versionResult = EmailLayoutVersion.CreateDraft(
            Id,
            nextVersionNumber,
            htmlStorageKey,
            htmlFileId,
            designJsonStorageKey,
            designJsonFileId,
            previewImageStorageKey,
            previewImageFileId,
            nowUtc
        );
        if (versionResult.IsFailure)
            return versionResult;

        _versions.Add(versionResult.Value);
        UpdatedAtUtc = nowUtc;
        return versionResult;
    }

    /// <summary>Publica una versión Draft y archiva automáticamente la que estaba Published (invariante: solo una a la vez).</summary>
    public Result PublishVersion(Guid versionId, Guid publishedByUserId, DateTime publishedAtUtc)
    {
        var version = _versions.Find(v => v.Id == versionId);
        if (version is null)
            return Result.Failure(
                new Error("EmailLayout.VersionNotFound", $"Version {versionId} was not found on this layout.")
            );

        var publishResult = version.Publish(publishedByUserId, publishedAtUtc);
        if (publishResult.IsFailure)
            return publishResult;

        foreach (var other in _versions)
            if (other.Id != versionId && other.Status == EmailVersionStatus.Published)
                other.Archive();

        UpdatedAtUtc = publishedAtUtc;
        return Result.Success();
    }

    public Result DeprecateLayout(DateTime updatedAtUtc)
    {
        if (Status == EmailContentStatus.Deleted)
            return Result.Failure(new Error("EmailLayout.Deleted", "A deleted layout cannot be deprecated."));

        if (Status == EmailContentStatus.Deprecated)
            return Result.Failure(new Error("EmailLayout.AlreadyDeprecated", "Layout is already deprecated."));

        Status = EmailContentStatus.Deprecated;
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }
}
