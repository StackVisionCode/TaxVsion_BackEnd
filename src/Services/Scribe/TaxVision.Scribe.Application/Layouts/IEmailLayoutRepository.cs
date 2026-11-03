using BuildingBlocks.Results;
using TaxVision.Scribe.Domain.Layouts;

namespace TaxVision.Scribe.Application.Layouts;

/// <summary>Lectura para el render pipeline (Fase 4) + CRUD de administración (Fase 5).</summary>
public interface IEmailLayoutRepository
{
    Task<Result<EmailLayout>> GetByIdAsync(Guid layoutId, CancellationToken ct = default);

    Task AddAsync(EmailLayout layout, CancellationToken ct = default);

    /// <summary>Layouts (System + Tenant) con alguna versión que apunta a <paramref name="fileId"/> — rechazos de CloudStorage.</summary>
    Task<IReadOnlyList<EmailLayout>> GetReferencingFileAsync(Guid fileId, CancellationToken ct = default);
}
