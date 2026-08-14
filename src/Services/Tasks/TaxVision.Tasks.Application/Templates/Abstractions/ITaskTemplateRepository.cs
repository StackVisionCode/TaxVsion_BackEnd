using BuildingBlocks.Results;
using TaxVision.Tasks.Domain.Templates;

namespace TaxVision.Tasks.Application.Templates.Abstractions;

public interface ITaskTemplateRepository
{
    void Add(TaskTemplate template);

    /// <summary>Trae la plantilla con sus pasos: aplicarla sin el guion no tiene sentido.</summary>
    Task<Result<TaskTemplate>> GetByIdAsync(Guid tenantId, Guid templateId, CancellationToken ct = default);

    Task<IReadOnlyList<TaskTemplate>> ListAsync(Guid tenantId, bool onlyActive, CancellationToken ct = default);

    /// <summary>
    /// Si esta plantilla ya se instanció para ese cliente y año. Es la única forma de no duplicar el
    /// encargo: los títulos se repiten entre plantillas y no identifican nada.
    /// </summary>
    Task<bool> WasAppliedAsync(
        Guid tenantId,
        Guid templateId,
        Guid? customerId,
        int? taxYear,
        CancellationToken ct = default
    );
}
