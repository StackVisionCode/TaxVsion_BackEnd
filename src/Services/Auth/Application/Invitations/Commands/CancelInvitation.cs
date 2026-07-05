using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Invitations.Commands;

public sealed record CancelInvitationCommand(Guid InvitationId, Guid CancelledByUserId);

public static class CancelInvitationHandler
{
    public static async Task<Result> Handle(
        CancelInvitationCommand command,
        IInvitationRepository invitations,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var actor = await users.GetByIdAsync(command.CancelledByUserId, ct);
        var invitation = await invitations.GetByIdAsync(command.InvitationId, ct);
        if (actor is null || invitation is null)
        {
            return Result.Failure(new Error("Invitation.NotFound", "Invitation does not exist."));
        }

        var canCancel =
            actor.IsActive
            && (
                actor.ActorType == UserActorType.PlatformAdmin
                || actor.ActorType == UserActorType.TenantAdmin && actor.TenantId == invitation.TenantId
            );

        if (!canCancel)
        {
            return Result.Failure(new Error("Invitation.Forbidden", "Actor cannot cancel this invitation."));
        }

        var result = invitation.Cancel(actor.Id, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await audit.AddAsync(
            AuthAuditLog.Record(
                invitation.TenantId,
                actor.Id,
                AuthAuditAction.InvitationCancelled,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Invitation",
                targetId: invitation.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
