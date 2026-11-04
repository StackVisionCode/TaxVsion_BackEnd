using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Participants;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Participants;

// Misma regla que al crear el enlace: el responsable debe estar activo y poder ejecutar las acciones de la versión.
public static class FormIntakeLinkResponsibleRules
{
    public static async Task<Result> EnsureEligibleAsync(
        Guid tenantId,
        Guid responsibleUserId,
        FormTemplateVersion version,
        IFormResponsibleUserValidator responsibleUsers,
        IFormActionAuthorizer authorizer,
        CancellationToken ct
    )
    {
        if (!await responsibleUsers.IsActiveAsync(tenantId, responsibleUserId, ct))
            return Result.Failure(FormParticipantErrors.IntakeResponsibleUnavailable);
        return await authorizer.EnsureResponsibleCanRunAsync(tenantId, responsibleUserId, version, ct);
    }
}
