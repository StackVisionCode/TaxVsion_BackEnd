using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;

// Escritura M2M autenticada hacia CloudStorage: el broker no prueba el origen. Un fallo es transitorio.
public interface IFormFileReassignmentClient
{
    Task<Result<IReadOnlyList<FormFileReassignmentResult>>> ReassignAsync(
        FormFileReassignmentRequest request,
        CancellationToken ct = default
    );
}
