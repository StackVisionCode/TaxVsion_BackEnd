using BuildingBlocks.Domain;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.ValueObjects;

namespace TaxVision.Tasks.ClientForms.Domain.Versions;

public sealed class FormTemplateVersion : AggregateRoot
{
    private FormTemplateVersion() { }

    public Guid FormTemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public int SchemaVersion { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public FormPurpose Purpose { get; private set; }
    public FormPresentation Presentation { get; private set; }
    public FormDefinitionDocument Definition { get; private set; } = default!;
    public ContentHash ContentHash { get; private set; } = default!;
    public int ComponentCount { get; private set; }
    public DateTime PublishedAtUtc { get; private set; }
    public Guid PublishedByUserId { get; private set; }

    internal static FormTemplateVersion Create(
        Guid tenantId,
        Guid formTemplateId,
        int versionNumber,
        string title,
        FormPurpose purpose,
        FormPresentation presentation,
        FormDefinitionDocument definition,
        ContentHash contentHash,
        int componentCount,
        Guid publishedByUserId,
        DateTime publishedAtUtc
    )
    {
        var version = new FormTemplateVersion
        {
            FormTemplateId = formTemplateId,
            VersionNumber = versionNumber,
            SchemaVersion = 1,
            Title = title,
            Purpose = purpose,
            Presentation = presentation,
            Definition = definition,
            ContentHash = contentHash,
            ComponentCount = componentCount,
            PublishedAtUtc = publishedAtUtc,
            PublishedByUserId = publishedByUserId,
        };
        version.SetTenant(tenantId);
        return version;
    }
}
