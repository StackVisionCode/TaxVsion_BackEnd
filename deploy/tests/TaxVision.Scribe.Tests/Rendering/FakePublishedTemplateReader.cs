using BuildingBlocks.Results;
using TaxVision.Scribe.Application.Layouts;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Tests.Rendering;

/// <summary>La misma lectura que el real, sobre los dobles de los repositorios: el filtrado real lo cubre la T-INT.</summary>
internal sealed class FakePublishedTemplateReader(IEmailTemplateRepository templates, IEmailLayoutRepository layouts)
    : IPublishedTemplateReader
{
    public async Task<Result<PublishedTemplate>> GetByKeyAsync(
        TemplateKey templateKey,
        Guid? tenantId,
        CancellationToken ct = default
    )
    {
        var template = await templates.GetByKeyAsync(templateKey, tenantId, ct);
        return template.IsFailure
            ? Result.Failure<PublishedTemplate>(template.Error)
            : Result.Success(
                new PublishedTemplate(
                    template.Value.TenantId,
                    template.Value.Scope,
                    template.Value.Channel,
                    template.Value.DispatchScopeOverride,
                    template.Value.Versions.FirstOrDefault(v => v.Status == EmailVersionStatus.Published)
                )
            );
    }

    public async Task<Result<PinnedLayout>> GetPinnedLayoutAsync(
        Guid layoutId,
        int versionNumber,
        CancellationToken ct = default
    )
    {
        var layout = await layouts.GetByIdAsync(layoutId, ct);
        return layout.IsFailure
            ? Result.Failure<PinnedLayout>(layout.Error)
            : Result.Success(
                new PinnedLayout(
                    layout.Value.LayoutKey.Value,
                    layout.Value.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber)
                )
            );
    }
}
