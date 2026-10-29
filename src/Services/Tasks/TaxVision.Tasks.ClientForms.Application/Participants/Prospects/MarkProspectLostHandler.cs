using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Prospects;

public static class MarkProspectLostHandler
{
    public const string AuditAction = "prospect.marked_lost";

    public static async Task<Result<ProspectResponse>> Handle(
        MarkProspectLostCommand command,
        IProspectReadRepository reads,
        IFormParticipantRepository participants,
        ProspectResponseMapper mapper,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var safe = StaffTextGuard.Validate(command.Reason);
        if (safe.IsFailure)
            return Result.Failure<ProspectResponse>(safe.Error);
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
        var lost = participant.Value.MarkLost(command.Reason, command.ByUserId, command.NowUtc);
        if (lost.IsFailure)
            return Result.Failure<ProspectResponse>(lost.Error);
        ProspectCommandSupport.Audit(scope, AuditAction, audit);
        await unitOfWork.SaveChangesAsync(ct);
        return await ProspectCommandSupport.ReadBackAsync(scope, reads, mapper, ct);
    }
}
