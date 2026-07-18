using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Scribe.Domain.Templates;

/// <summary>
/// Versión inmutable (una vez Published) de un EmailTemplate. El HTML/design/preview viven en
/// CloudStorage — acá solo las storage keys. <see cref="LayoutId"/>/<see cref="LayoutVersionNumber"/>
/// son un snapshot fijo: si el layout base saca una v2, esta version sigue apuntando a la que tenía al
/// publicarse (re-renderizar un correo viejo debe reproducir el mismo HTML exacto).
/// </summary>
public sealed class EmailTemplateVersion : BaseEntity
{
    private readonly List<TemplateVariableDefinition> _variableDefinitions = [];

    private EmailTemplateVersion() { }

    public Guid EmailTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public EmailVersionStatus Status { get; private set; }
    public string Subject { get; private set; } = default!;
    public string HtmlStorageKey { get; private set; } = default!;
    public Guid HtmlFileId { get; private set; }
    public string? TextStorageKey { get; private set; }
    public Guid? TextFileId { get; private set; }
    public string? DesignJsonStorageKey { get; private set; }
    public Guid? DesignJsonFileId { get; private set; }
    public string? PreviewImageStorageKey { get; private set; }
    public Guid? PreviewImageFileId { get; private set; }
    public Guid LayoutId { get; private set; }
    public int LayoutVersionNumber { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public Guid? PublishedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public IReadOnlyList<TemplateVariableDefinition> VariableDefinitions => _variableDefinitions.AsReadOnly();

    internal static Result<EmailTemplateVersion> CreateDraft(
        Guid emailTemplateId,
        int versionNumber,
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
        if (string.IsNullOrWhiteSpace(subject) || subject.Length > 500)
            return Result.Failure<EmailTemplateVersion>(
                new Error("EmailTemplateVersion.Subject", "Subject is required and must be at most 500 characters.")
            );

        if (string.IsNullOrWhiteSpace(htmlStorageKey) || htmlStorageKey.Length > 500)
            return Result.Failure<EmailTemplateVersion>(
                new Error(
                    "EmailTemplateVersion.HtmlStorageKey",
                    "HtmlStorageKey is required and must be at most 500 characters."
                )
            );

        if (htmlFileId == Guid.Empty)
            return Result.Failure<EmailTemplateVersion>(
                new Error("EmailTemplateVersion.HtmlFileId", "HtmlFileId is required.")
            );

        if (layoutId == Guid.Empty)
            return Result.Failure<EmailTemplateVersion>(
                new Error(
                    "EmailTemplateVersion.LayoutRequired",
                    "A base layout is required — standalone templates are not allowed."
                )
            );

        if (layoutVersionNumber <= 0)
            return Result.Failure<EmailTemplateVersion>(
                new Error(
                    "EmailTemplateVersion.LayoutVersionRequired",
                    "A published layout version number is required."
                )
            );

        var version = new EmailTemplateVersion
        {
            Id = Guid.NewGuid(),
            EmailTemplateId = emailTemplateId,
            VersionNumber = versionNumber,
            Status = EmailVersionStatus.Draft,
            Subject = subject.Trim(),
            HtmlStorageKey = htmlStorageKey,
            HtmlFileId = htmlFileId,
            TextStorageKey = textStorageKey,
            TextFileId = textFileId,
            DesignJsonStorageKey = designJsonStorageKey,
            DesignJsonFileId = designJsonFileId,
            PreviewImageStorageKey = previewImageStorageKey,
            PreviewImageFileId = previewImageFileId,
            LayoutId = layoutId,
            LayoutVersionNumber = layoutVersionNumber,
            CreatedAtUtc = nowUtc,
        };

        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in variableDefinitions)
        {
            var variableResult = TemplateVariableDefinition.Create(
                version.Id,
                definition.Name,
                definition.Type,
                definition.Required,
                definition.DefaultValue,
                definition.Description
            );
            if (variableResult.IsFailure)
                return Result.Failure<EmailTemplateVersion>(variableResult.Error);

            if (!seenNames.Add(variableResult.Value.Name))
                return Result.Failure<EmailTemplateVersion>(
                    new Error(
                        "EmailTemplateVersion.DuplicateVariable",
                        $"Variable '{variableResult.Value.Name}' is declared more than once."
                    )
                );

            version._variableDefinitions.Add(variableResult.Value);
        }

        return Result.Success(version);
    }

    internal Result Publish(Guid publishedByUserId, DateTime publishedAtUtc)
    {
        if (Status != EmailVersionStatus.Draft)
            return Result.Failure(new Error("EmailTemplateVersion.NotDraft", "Only draft versions can be published."));

        Status = EmailVersionStatus.Published;
        PublishedByUserId = publishedByUserId;
        PublishedAtUtc = publishedAtUtc;
        return Result.Success();
    }

    internal void Archive() => Status = EmailVersionStatus.Archived;
}
