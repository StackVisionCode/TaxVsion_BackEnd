using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Templates;

public enum EmailTemplateStatus
{
    Draft,
    Active,
    Archived,
}

/// <summary>
/// Plantilla de correo. La BD guarda solo metadatos y las claves de almacenamiento; el HTML, el
/// design JSON, los previews y los assets viven en CloudStorage. Cada edición crea una nueva versión;
/// solo una versión puede estar publicada a la vez. Las plantillas publicadas no se borran: se archivan.
/// </summary>
/// <remarks>Migration target: <b>Scribe</b>. See <c>Responsibility_Map.md</c>. Se elimina de Notification en Fase 7.</remarks>
public sealed class EmailTemplate : BaseEntity
{
    private EmailTemplate() { }

    public Guid? TenantId { get; private set; }
    public EmailScope Scope { get; private set; }
    public string TemplateKey { get; private set; } = default!;
    public string Subject { get; private set; } = default!;
    public string? Description { get; private set; }
    public string? Category { get; private set; }

    /// <summary>Variables permitidas, serializadas como array JSON de nombres.</summary>
    public string VariablesJson { get; private set; } = "[]";

    public EmailTemplateStatus Status { get; private set; }
    public Guid? CurrentVersionId { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }

    public static Result<EmailTemplate> Create(
        EmailScope scope,
        Guid? tenantId,
        string templateKey,
        string subject,
        string? description,
        string? category,
        string variablesJson,
        Guid? createdByUserId
    )
    {
        if (scope == EmailScope.Tenant && (tenantId is null || tenantId == Guid.Empty))
            return Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.Tenant", "Tenant templates require a tenant id.")
            );

        if (scope == EmailScope.System && tenantId is not null)
            return Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.Scope", "System templates must not carry a tenant id.")
            );

        if (string.IsNullOrWhiteSpace(templateKey))
            return Result.Failure<EmailTemplate>(new Error("EmailTemplate.Key", "Template key is required."));

        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure<EmailTemplate>(new Error("EmailTemplate.Subject", "Subject is required."));

        var template = new EmailTemplate
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            TenantId = scope == EmailScope.Tenant ? tenantId : null,
            TemplateKey = templateKey.Trim().ToLowerInvariant(),
            Subject = subject.Trim(),
            Description = Normalize(description),
            Category = Normalize(category),
            VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? "[]" : variablesJson,
            Status = EmailTemplateStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        return Result.Success(template);
    }

    public Result UpdateMetadata(string subject, string? description, string? category, string variablesJson)
    {
        if (Status == EmailTemplateStatus.Archived)
            return Result.Failure(new Error("EmailTemplate.Archived", "An archived template cannot be edited."));

        if (string.IsNullOrWhiteSpace(subject))
            return Result.Failure(new Error("EmailTemplate.Subject", "Subject is required."));

        Subject = subject.Trim();
        Description = Normalize(description);
        Category = Normalize(category);
        VariablesJson = string.IsNullOrWhiteSpace(variablesJson) ? "[]" : variablesJson;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>Marca una versión como la publicada/activa. La orquestación (superseder la anterior) la hace el handler.</summary>
    public Result MarkPublished(Guid versionId)
    {
        if (Status == EmailTemplateStatus.Archived)
            return Result.Failure(new Error("EmailTemplate.Archived", "An archived template cannot be published."));

        CurrentVersionId = versionId;
        Status = EmailTemplateStatus.Active;
        PublishedAtUtc = DateTime.UtcNow;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Archive()
    {
        if (Status == EmailTemplateStatus.Archived)
            return Result.Failure(new Error("EmailTemplate.Archived", "Template is already archived."));

        Status = EmailTemplateStatus.Archived;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
