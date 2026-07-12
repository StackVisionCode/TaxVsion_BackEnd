using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Notification.Domain.Emailing.Templates;

public enum EmailTemplateVersionStatus
{
    Draft,
    Published,
    Superseded,
}

/// <summary>
/// Versión inmutable de una plantilla. Referencia por claves de almacenamiento el HTML, el design JSON
/// y el preview en CloudStorage (no se guarda el contenido en la BD). El subject renderizable se
/// versiona junto con el cuerpo.
/// </summary>
public sealed class EmailTemplateVersion : BaseEntity
{
    private EmailTemplateVersion() { }

    public Guid TemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public string SubjectTemplate { get; private set; } = default!;
    public string HtmlStorageKey { get; private set; } = default!;
    public Guid HtmlFileId { get; private set; }
    public string? DesignStorageKey { get; private set; }
    public Guid? DesignFileId { get; private set; }
    public string? PreviewStorageKey { get; private set; }
    public Guid? PreviewFileId { get; private set; }
    public EmailTemplateVersionStatus Status { get; private set; }
    public Guid? CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static Result<EmailTemplateVersion> Create(
        Guid templateId,
        int versionNumber,
        string subjectTemplate,
        string htmlStorageKey,
        Guid htmlFileId,
        string? designStorageKey,
        Guid? designFileId,
        string? previewStorageKey,
        Guid? previewFileId,
        Guid? createdByUserId
    )
    {
        if (string.IsNullOrWhiteSpace(subjectTemplate))
            return Result.Failure<EmailTemplateVersion>(
                new Error("EmailTemplate.Subject", "Subject template is required.")
            );

        if (string.IsNullOrWhiteSpace(htmlStorageKey) || htmlFileId == Guid.Empty)
            return Result.Failure<EmailTemplateVersion>(
                new Error("EmailTemplate.Html", "HTML storage reference is required.")
            );

        var version = new EmailTemplateVersion
        {
            Id = Guid.NewGuid(),
            TemplateId = templateId,
            VersionNumber = versionNumber,
            SubjectTemplate = subjectTemplate,
            HtmlStorageKey = htmlStorageKey,
            HtmlFileId = htmlFileId,
            DesignStorageKey = designStorageKey,
            DesignFileId = designFileId,
            PreviewStorageKey = previewStorageKey,
            PreviewFileId = previewFileId,
            Status = EmailTemplateVersionStatus.Draft,
            CreatedByUserId = createdByUserId,
            CreatedAtUtc = DateTime.UtcNow,
        };
        return Result.Success(version);
    }

    public void MarkPublished() => Status = EmailTemplateVersionStatus.Published;

    public void MarkSuperseded() => Status = EmailTemplateVersionStatus.Superseded;
}
