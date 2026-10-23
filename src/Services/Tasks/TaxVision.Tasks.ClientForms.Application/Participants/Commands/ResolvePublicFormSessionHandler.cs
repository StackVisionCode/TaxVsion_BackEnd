using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Participants;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class ResolvePublicFormSessionHandler
{
    public static async Task<Result<ParticipantContext>> Handle(
        ResolvePublicFormSessionCommand command,
        IFormAccessSessionRepository sessions,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IClientFormsPublicCapabilityEvaluator capabilities,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.SessionToken) || string.IsNullOrWhiteSpace(command.RequestHost))
            return Invalid();
        var session = await sessions.GetByTokenHashAsync(security.HashToken(command.SessionToken), ct);
        if (
            session is null
            || !session.IsActiveAt(command.NowUtc)
            || !await hostValidator.MatchesTenantAsync(session.TenantId, command.RequestHost, ct)
            || !await capabilities.IsExternalParticipantAvailableAsync(session.TenantId, ct)
        )
            return Invalid();
        if (session.Touch(command.NowUtc).IsFailure)
            return Invalid();
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(
            ParticipantContext.External(session.TenantId, session.ParticipantId, session.FormRequestId, session.Id)
        );
    }

    private static Result<ParticipantContext> Invalid() =>
        Result.Failure<ParticipantContext>(FormParticipantErrors.SessionInvalid);
}
