using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Participants.Commands;

public static class RevokePublicFormSessionHandler
{
    public static async Task<Result> Handle(
        RevokePublicFormSessionCommand command,
        IFormAccessSessionRepository sessions,
        IFormExternalAccessSecurity security,
        IFormPublicTenantHostValidator hostValidator,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(command.SessionToken) || string.IsNullOrWhiteSpace(command.RequestHost))
            return Result.Success();
        var session = await sessions.GetByTokenHashAsync(security.HashToken(command.SessionToken), ct);
        if (session is null || !await hostValidator.MatchesTenantAsync(session.TenantId, command.RequestHost, ct))
            return Result.Success();
        session.Revoke(command.NowUtc);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
