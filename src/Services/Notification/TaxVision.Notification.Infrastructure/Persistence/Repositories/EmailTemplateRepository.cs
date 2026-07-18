using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing;
using TaxVision.Notification.Domain.Emailing.Templates;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class EmailTemplateRepository(NotificationDbContext db) : IEmailTemplateRepository
{
    public async Task AddAsync(EmailTemplate template, CancellationToken ct = default) =>
        await db.EmailTemplates.AddAsync(template, ct);

    public async Task AddVersionAsync(EmailTemplateVersion version, CancellationToken ct = default) =>
        await db.EmailTemplateVersions.AddAsync(version, ct);

    public async Task<EmailTemplate?> GetByIdAsync(Guid id, Guid? tenantId, CancellationToken ct = default)
    {
        var template = await db.EmailTemplates.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (template is null)
            return null;

        // Aislamiento: las System son visibles para todos; las de tenant solo para su dueño.
        if (template.Scope == EmailScope.Tenant && template.TenantId != tenantId)
            return null;

        return template;
    }

    public async Task<EmailTemplate?> GetByKeyAsync(
        EmailScope scope,
        Guid? tenantId,
        string templateKey,
        CancellationToken ct = default
    )
    {
        var key = templateKey.Trim().ToLowerInvariant();
        return await db.EmailTemplates.FirstOrDefaultAsync(
            t => t.Scope == scope && t.TenantId == tenantId && t.TemplateKey == key,
            ct
        );
    }

    public async Task<EmailTemplateVersion?> GetVersionAsync(
        Guid templateId,
        Guid versionId,
        CancellationToken ct = default
    ) => await db.EmailTemplateVersions.FirstOrDefaultAsync(v => v.Id == versionId && v.TemplateId == templateId, ct);

    public async Task<int> GetNextVersionNumberAsync(Guid templateId, CancellationToken ct = default)
    {
        var max = await db
            .EmailTemplateVersions.Where(v => v.TemplateId == templateId)
            .Select(v => (int?)v.VersionNumber)
            .MaxAsync(ct);
        return (max ?? 0) + 1;
    }

    public async Task<IReadOnlyList<EmailTemplate>> ListAsync(
        Guid? tenantId,
        bool includeSystem,
        CancellationToken ct = default
    ) =>
        await db
            .EmailTemplates.AsNoTracking()
            .Where(t =>
                (tenantId != null && t.Scope == EmailScope.Tenant && t.TenantId == tenantId)
                || (includeSystem && t.Scope == EmailScope.System)
            )
            .OrderBy(t => t.TemplateKey)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<EmailTemplateVersion>> ListVersionsAsync(
        Guid templateId,
        CancellationToken ct = default
    ) =>
        await db
            .EmailTemplateVersions.AsNoTracking()
            .Where(v => v.TemplateId == templateId)
            .OrderByDescending(v => v.VersionNumber)
            .ToListAsync(ct);
}
