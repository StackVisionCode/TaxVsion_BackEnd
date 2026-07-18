using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Domain.Templates;

/// <summary>
/// Plantilla de correo (System o Tenant). Raíz de agregado: contiene todas sus
/// <see cref="EmailTemplateVersion"/> y es dueña del invariante "solo una version Published a la vez"
/// (<see cref="PublishVersion"/> supersede automáticamente la Published anterior).
/// </summary>
public sealed class EmailTemplate : BaseEntity
{
    private readonly List<EmailTemplateVersion> _versions = [];

    private EmailTemplate() { }

    public Guid? TenantId { get; private set; }
    public TemplateScope Scope { get; private set; }
    public TemplateKey TemplateKey { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public EmailContentStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }

    public IReadOnlyList<EmailTemplateVersion> Versions => _versions.AsReadOnly();

    public static Result<EmailTemplate> CreateNew(
        TemplateScope scope,
        Guid? tenantId,
        TemplateKey templateKey,
        string name,
        string? description,
        Guid createdByUserId,
        DateTime createdAtUtc
    )
    {
        if (scope == TemplateScope.Tenant && tenantId is null)
            return Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.TenantRequired", "TenantId is required for Tenant-scoped templates.")
            );

        if (scope == TemplateScope.System && tenantId is not null)
            return Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.TenantNotAllowed", "TenantId must be null for System-scoped templates.")
            );

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.Name", "Name is required and must be at most 200 characters.")
            );

        return Result.Success(
            new EmailTemplate
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                Scope = scope,
                TemplateKey = templateKey,
                Name = name.Trim(),
                Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
                Status = EmailContentStatus.Active,
                CreatedByUserId = createdByUserId,
                CreatedAtUtc = createdAtUtc,
            }
        );
    }

    public Result<EmailTemplateVersion> AddDraftVersion(
        string subject,
        string htmlStorageKey,
        Guid htmlFileId,
        string? textStorageKey,
        Guid? textFileId,
        string? designJsonStorageKey,
        Guid? designJsonFileId,
        string? previewImageStorageKey,
        Guid? previewImageFileId,
        Guid layoutId,
        int layoutVersionNumber,
        IReadOnlyList<(
            string Name,
            VariableType Type,
            bool Required,
            string? DefaultValue,
            string? Description
        )> variableDefinitions,
        DateTime nowUtc
    )
    {
        if (Status != EmailContentStatus.Active)
            return Result.Failure<EmailTemplateVersion>(
                new Error("EmailTemplate.NotActive", "Cannot add a version to a non-active template.")
            );

        var nextVersionNumber = 1;
        foreach (var version in _versions)
            if (version.VersionNumber >= nextVersionNumber)
                nextVersionNumber = version.VersionNumber + 1;

        var versionResult = EmailTemplateVersion.CreateDraft(
            Id,
            nextVersionNumber,
            subject,
            htmlStorageKey,
            htmlFileId,
            textStorageKey,
            textFileId,
            designJsonStorageKey,
            designJsonFileId,
            previewImageStorageKey,
            previewImageFileId,
            layoutId,
            layoutVersionNumber,
            variableDefinitions,
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
                new Error("EmailTemplate.VersionNotFound", $"Version {versionId} was not found on this template.")
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

    /// <summary>
    /// Purga (elimina definitivamente) las versiones Archived creadas antes de
    /// <paramref name="cutoffUtc"/> — Fase 10, retention job. Nunca toca la versión Published ni
    /// ninguna Draft, sin importar su edad — solo Archived es candidata. Los blobs de CloudStorage
    /// que esas versiones referenciaban NO se borran acá: Scribe solo tiene subida/lectura
    /// (<c>ITemplateStorageService</c>), no delete; la limpieza de blobs huérfanos es
    /// responsabilidad del recycle bin de CloudStorage, no de Scribe.
    /// </summary>
    public IReadOnlyList<Guid> PurgeArchivedVersionsOlderThan(DateTime cutoffUtc)
    {
        var purgedIds = new List<Guid>();
        for (var i = _versions.Count - 1; i >= 0; i--)
        {
            var version = _versions[i];
            if (version.Status == EmailVersionStatus.Archived && version.CreatedAtUtc < cutoffUtc)
            {
                purgedIds.Add(version.Id);
                _versions.RemoveAt(i);
            }
        }
        return purgedIds;
    }

    public Result DeprecateTemplate(DateTime updatedAtUtc)
    {
        if (Status == EmailContentStatus.Deleted)
            return Result.Failure(new Error("EmailTemplate.Deleted", "A deleted template cannot be deprecated."));

        if (Status == EmailContentStatus.Deprecated)
            return Result.Failure(new Error("EmailTemplate.AlreadyDeprecated", "Template is already deprecated."));

        Status = EmailContentStatus.Deprecated;
        UpdatedAtUtc = updatedAtUtc;
        return Result.Success();
    }
}
