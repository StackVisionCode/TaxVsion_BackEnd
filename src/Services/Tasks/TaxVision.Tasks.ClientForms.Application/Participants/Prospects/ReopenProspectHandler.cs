using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

public static class ReopenProspectHandler
{
    public const string AuditAction = "prospect.reopened";

    // Idempotente: reabrir uno que no está perdido devuelve la fila sin auditar ni guardar.
    public static async Task<Result<ProspectResponse>> Handle(
        ReopenProspectCommand command,
        IProspectReadRepository reads,
        IFormParticipantRepository participants,
        ProspectResponseMapper mapper,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var scope = new ProspectScope(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.CanStartCustomerCreation,
            command.ParticipantId
        );
        var participant = await ProspectCommandSupport.LoadVisibleAsync(scope, reads, participants, ct);
        if (participant.IsFailure)
            return Result.Failure<ProspectResponse>(participant.Error);
        var wasLost = participant.Value.IsLost;
        var reopened = participant.Value.Reopen(command.ByUserId, command.NowUtc);
        if (reopened.IsFailure)
            return Result.Failure<ProspectResponse>(reopened.Error);
        if (wasLost)
        {
            ProspectCommandSupport.Audit(scope, AuditAction, audit);
            await unitOfWork.SaveChangesAsync(ct);
        }
        return await ProspectCommandSupport.ReadBackAsync(scope, reads, mapper, ct);
    }
}
