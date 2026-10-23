using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;

public interface IFormActionAuthorizer
{
    // El responsable ejecuta las acciones más tarde, en su nombre: debe tener hoy los permisos de cada una.
    Task<Result> EnsureResponsibleCanRunAsync(
        Guid tenantId,
        Guid responsibleUserId,
        FormTemplateVersion version,
        CancellationToken ct = default
    );
}
