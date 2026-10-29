namespace TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

public interface IFormResponsibleUserValidator
{
    Task<bool> IsActiveAsync(Guid tenantId, Guid userId, CancellationToken ct = default);

    // Misma proyección local que autoriza las requests HTTP del usuario; sin fila, no tiene permisos.
    Task<bool> HasPermissionsAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<string> permissions,
        CancellationToken ct = default
    );
}
