using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Rendering;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

/// <summary>
/// IgnoreQueryFilters: el render es M2M (ActorType.Service, sin claim tenant_id), así que el ITenantContext
/// ambiente nunca refleja el tenant del render. El aislamiento lo da el tenantId explícito de la consulta.
/// </summary>
public sealed class PublishedTemplateReader(ScribeDbContext dbContext) : IPublishedTemplateReader
{
    public async Task<Result<PublishedTemplate>> GetByKeyAsync(
        TemplateKey templateKey,
        Guid? tenantId,
        CancellationToken ct = default
    )
    {
        EmailTemplate? template = null;
        if (tenantId is not null)
            template = await Templates.FirstOrDefaultAsync(
                t => t.Scope == TemplateScope.Tenant && t.TenantId == tenantId && t.TemplateKey == templateKey,
                ct
            );

        template ??= await Templates.FirstOrDefaultAsync(
            t => t.Scope == TemplateScope.System && t.TemplateKey == templateKey,
            ct
        );

        return template is null
            ? Result.Failure<PublishedTemplate>(
                new Error("EmailTemplate.NotFound", $"No template found for key '{templateKey.Value}'.")
            )
            : Result.Success(
                new PublishedTemplate(
                    template.TenantId,
                    template.Scope,
                    template.Channel,
                    template.DispatchScopeOverride,
                    template.Versions.SingleOrDefault()
                )
            );
    }

    public async Task<Result<PinnedLayout>> GetPinnedLayoutAsync(
        Guid layoutId,
        int versionNumber,
        CancellationToken ct = default
    )
    {
        var layout = await dbContext
            .EmailLayouts.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(l => l.Versions.Where(v => v.VersionNumber == versionNumber))
            .FirstOrDefaultAsync(l => l.Id == layoutId, ct);

        return layout is null
            ? Result.Failure<PinnedLayout>(new Error("EmailLayout.NotFound", $"Layout {layoutId} was not found."))
            : Result.Success(new PinnedLayout(layout.LayoutKey.Value, layout.Versions.SingleOrDefault()));
    }

    // El índice único filtrado garantiza una sola Published por plantilla.
    private IQueryable<EmailTemplate> Templates =>
        dbContext
            .EmailTemplates.IgnoreQueryFilters()
            .AsNoTracking()
            .Include(t => t.Versions.Where(v => v.Status == EmailVersionStatus.Published))
                .ThenInclude(v => v.VariableDefinitions);
}
