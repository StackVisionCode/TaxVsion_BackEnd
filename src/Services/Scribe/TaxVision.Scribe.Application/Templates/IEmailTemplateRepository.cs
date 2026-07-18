using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.Templates;
using TaxVision.Scribe.Domain.ValueObjects;

namespace TaxVision.Scribe.Application.Templates;

/// <summary>Lectura para el render pipeline (Fase 4) + CRUD de administración (Fase 5).</summary>
public interface IEmailTemplateRepository
{
    /// <summary>
    /// Prioriza el template Tenant (si <paramref name="tenantId"/> tiene uno con esa key) y cae al
    /// System — el mismo TemplateKey puede existir en ambos scopes, cada uno con su propio índice único.
    /// </summary>
    Task<Result<EmailTemplate>> GetByKeyAsync(TemplateKey templateKey, Guid? tenantId, CancellationToken ct = default);

    Task<Result<EmailTemplate>> GetByIdAsync(Guid templateId, CancellationToken ct = default);

    /// <summary>
    /// Resuelve una versión directamente por su Id (Preview/Validate reciben solo el versionId, no el
    /// templateId) junto con el template dueño — necesario para TenantId (auth de CloudStorage) y para
    /// invocar los métodos del aggregate root (Publish/Archive) sin una segunda consulta.
    /// </summary>
    Task<Result<(EmailTemplate Template, EmailTemplateVersion Version)>> GetVersionByIdAsync(
        Guid versionId,
        CancellationToken ct = default
    );

    Task AddAsync(EmailTemplate template, CancellationToken ct = default);

    /// <summary>Todas las versiones Published de todos los templates (System + Tenant) — usado por el warm-up de arranque (Fase 6), no por el render pipeline en caliente.</summary>
    Task<IReadOnlyList<(EmailTemplate Template, EmailTemplateVersion Version)>> GetAllPublishedAsync(
        CancellationToken ct = default
    );

    /// <summary>
    /// Templates con al menos una versión Archived creada antes de <paramref name="cutoffUtc"/> —
    /// Fase 10, retention job. Limitado a <paramref name="batchSize"/> templates por llamada para no
    /// cargar todo el catálogo de una vez.
    /// </summary>
    Task<IReadOnlyList<EmailTemplate>> GetWithArchivedVersionsOlderThanAsync(
        DateTime cutoffUtc,
        int batchSize,
        CancellationToken ct = default
    );
}
