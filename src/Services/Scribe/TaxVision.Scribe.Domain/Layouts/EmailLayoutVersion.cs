using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.Layouts;

/// <summary>
/// Versión inmutable (una vez Published) de un EmailLayout. Sin Subject/TextStorageKey — el layout
/// solo envuelve el HTML del body vía el placeholder <c>{{ body }}</c> (contrato validado en Fase 4).
/// </summary>
public sealed class EmailLayoutVersion : BaseEntity
{
    private EmailLayoutVersion() { }

    public Guid EmailLayoutId { get; private set; }
    public int VersionNumber { get; private set; }
    public EmailVersionStatus Status { get; private set; }
    public string HtmlStorageKey { get; private set; } = default!;
    public Guid HtmlFileId { get; private set; }
    public string? DesignJsonStorageKey { get; private set; }
    public Guid? DesignJsonFileId { get; private set; }
    public string? PreviewImageStorageKey { get; private set; }
    public Guid? PreviewImageFileId { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    internal static Result<EmailLayoutVersion> CreateDraft(
        Guid emailLayoutId,
        int versionNumber,
        string htmlStorageKey,
        Guid htmlFileId,
        string? designJsonStorageKey,
        Guid? designJsonFileId,
        string? previewImageStorageKey,
        Guid? previewImageFileId,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(htmlStorageKey) || htmlStorageKey.Length > 500)
            return Result.Failure<EmailLayoutVersion>(
                new Error(
                    "EmailLayoutVersion.HtmlStorageKey",
                    "HtmlStorageKey is required and must be at most 500 characters."
                )
            );

        if (htmlFileId == Guid.Empty)
            return Result.Failure<EmailLayoutVersion>(
                new Error("EmailLayoutVersion.HtmlFileId", "HtmlFileId is required.")
            );

        return Result.Success(
            new EmailLayoutVersion
            {
                Id = Guid.NewGuid(),
                EmailLayoutId = emailLayoutId,
                VersionNumber = versionNumber,
                Status = EmailVersionStatus.Draft,
                HtmlStorageKey = htmlStorageKey,
                HtmlFileId = htmlFileId,
                DesignJsonStorageKey = designJsonStorageKey,
                DesignJsonFileId = designJsonFileId,
                PreviewImageStorageKey = previewImageStorageKey,
                PreviewImageFileId = previewImageFileId,
                CreatedAtUtc = nowUtc,
            }
        );
    }

    internal Result Publish(Guid publishedByUserId, DateTime publishedAtUtc)
    {
        if (Status != EmailVersionStatus.Draft)
            return Result.Failure(new Error("EmailLayoutVersion.NotDraft", "Only draft versions can be published."));

        Status = EmailVersionStatus.Published;
        PublishedByUserId = publishedByUserId;
        PublishedAtUtc = publishedAtUtc;
        return Result.Success();
    }

    internal void Archive() => Status = EmailVersionStatus.Archived;
}
