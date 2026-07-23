using BuildingBlocks.Results;
using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Templates;
using TaxVision.Scribe.Domain;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

/// <summary>
/// Solo lectura: el mismo TemplateKey puede existir a la vez como override Tenant y como default
/// System (el índice único es por Scope), así que se prioriza Tenant y se cae a System — mismo
/// criterio de prioridad que <see cref="Application.EventMappings.EventTemplateResolver"/>.
/// </summary>
public sealed class EmailTemplateRepository(ScribeDbContext dbContext) : IEmailTemplateRepository
{
    // IgnoreQueryFilters: llamado exclusivamente desde el pipeline de render M2M
    // (FluidTemplateRenderer ← RenderController, ActorType.Service — sin claim tenant_id, así que
    // el ITenantContext ambiente nunca refleja el tenant real del render). El aislamiento acá lo da
    // el parámetro tenantId explícito de la query, no el filtro global (RBAC Fase 5).
    public async Task<Result<EmailTemplate>> GetByKeyAsync(
        TemplateKey templateKey,
        Guid? tenantId,
        CancellationToken ct = default
    )
    {
        EmailTemplate? template = null;
        if (tenantId is not null)
            template = await dbContext
                .EmailTemplates.IgnoreQueryFilters()
                .Include(t => t.Versions)
                .FirstOrDefaultAsync(
                    t => t.Scope == TemplateScope.Tenant && t.TenantId == tenantId && t.TemplateKey == templateKey,
                    ct
                );

        template ??= await dbContext
            .EmailTemplates.IgnoreQueryFilters()
            .Include(t => t.Versions)
            .FirstOrDefaultAsync(t => t.Scope == TemplateScope.System && t.TemplateKey == templateKey, ct);

        return template is null
            ? Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.NotFound", $"No template found for key '{templateKey.Value}'.")
            )
            : Result.Success(template);
    }

    // IgnoreQueryFilters: llamado desde handlers de Wolverine vía bus.InvokeAsync (AddEmailTemplateDraftVersionHandler,
    // PublishEmailTemplateVersionHandler, GetVersionByIdAsync de abajo, etc.) — el TenantContext ambiente puede
    // llegar vacío al scope de DI del handler aunque el middleware HTTP ya resolvió el tenant correctamente en el
    // scope de la request, y EmailTemplate es INullableTenantOwned (System-or-Tenant scoped) — con TenantContext
    // vacío el filtro ambiental solo deja pasar filas System-scope, así que un lookup por Id puro de un template
    // Tenant-scope siempre devolvía NotFound. Es seguro: todos los llamadores ya validan
    // template.TenantId != command.TenantId post-fetch vía AuthorizeWrite (ej. AddEmailTemplateDraftVersionHandler,
    // PublishEmailTemplateVersionHandler) — el filtro ambiental era redundante con esa guarda (RBAC Fase 5).
    public async Task<Result<EmailTemplate>> GetByIdAsync(Guid templateId, CancellationToken ct = default)
    {
        var template = await dbContext
            .EmailTemplates.IgnoreQueryFilters()
            .Include(t => t.Versions)
                .ThenInclude(v => v.VariableDefinitions)
            .FirstOrDefaultAsync(t => t.Id == templateId, ct);

        return template is null
            ? Result.Failure<EmailTemplate>(
                new Error("EmailTemplate.NotFound", $"Template {templateId} was not found.")
            )
            : Result.Success(template);
    }

    public async Task<Result<(EmailTemplate Template, EmailTemplateVersion Version)>> GetVersionByIdAsync(
        Guid versionId,
        CancellationToken ct = default
    )
    {
        var version = await dbContext
            .EmailTemplateVersions.AsNoTracking()
            .FirstOrDefaultAsync(v => v.Id == versionId, ct);
        if (version is null)
            return Result.Failure<(EmailTemplate, EmailTemplateVersion)>(
                new Error("EmailTemplateVersion.NotFound", $"Version {versionId} was not found.")
            );

        var templateResult = await GetByIdAsync(version.EmailTemplateId, ct);
        if (templateResult.IsFailure)
            return Result.Failure<(EmailTemplate, EmailTemplateVersion)>(templateResult.Error);

        var trackedVersion = templateResult.Value.Versions.First(v => v.Id == versionId);
        return Result.Success((templateResult.Value, trackedVersion));
    }

    public async Task AddAsync(EmailTemplate template, CancellationToken ct = default) =>
        await dbContext.EmailTemplates.AddAsync(template, ct);

    // IgnoreQueryFilters: TemplateWarmupService (job de arranque) precalienta el cache de render
    // para TODOS los templates System + de TODOS los tenants — cross-tenant genuino, nunca sirve
    // una request autenticada (RBAC Fase 5).
    public async Task<IReadOnlyList<(EmailTemplate Template, EmailTemplateVersion Version)>> GetAllPublishedAsync(
        CancellationToken ct = default
    )
    {
        var templates = await dbContext
            .EmailTemplates.IgnoreQueryFilters()
            .Include(t => t.Versions)
            .Where(t => t.Versions.Any(v => v.Status == EmailVersionStatus.Published))
            .ToListAsync(ct);

        return templates
            .Select(t => (Template: t, Version: t.Versions.First(v => v.Status == EmailVersionStatus.Published)))
            .ToList();
    }

    // IgnoreQueryFilters: ScribeRetentionScheduler purga versiones Archived viejas de TODOS los
    // templates System + de TODOS los tenants — cross-tenant genuino, nunca sirve una request
    // autenticada (RBAC Fase 5).
    public async Task<IReadOnlyList<EmailTemplate>> GetWithArchivedVersionsOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    ) =>
        await dbContext
            .EmailTemplates.IgnoreQueryFilters()
            .Include(t => t.Versions)
            .Where(t => t.Versions.Any(v => v.Status == EmailVersionStatus.Archived && v.CreatedAtUtc < cutoffUtc))
            .Take(batchSize)
            .ToListAsync(ct);
}
