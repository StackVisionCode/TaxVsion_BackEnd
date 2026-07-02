using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Invitations.Commands;

/// <summary>
/// Reenvía una invitación pendiente regenerando su token (el anterior queda inválido).
/// Máximo Invitation.MaxResends reenvíos.
/// </summary>
public sealed record ResendInvitationCommand(
    Guid InvitationId,
    Guid RequestedByUserId,
    Guid TenantId);

public static class ResendInvitationHandler
{
    public static async Task<Result> Handle(
        ResendInvitationCommand command,
        IInvitationRepository invitations,
        IUserRepository users,
        ITenantRegistry tenants,
        IInvitationTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IOptions<InvitationOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var actor = await users.GetByIdAsync(command.RequestedByUserId, ct);
        var invitation = await invitations.GetByIdAsync(command.InvitationId, ct);
        if (actor is null || invitation is null)
            return Result.Failure(new Error("Invitation.NotFound", "Invitation does not exist."));

        var canResend =
            actor.IsActive &&
            (actor.ActorType == UserActorType.PlatformAdmin ||
             (actor.ActorType == UserActorType.TenantAdmin &&
              actor.TenantId == invitation.TenantId));
        if (!canResend || invitation.TenantId != command.TenantId &&
            actor.ActorType != UserActorType.PlatformAdmin)
        {
            return Result.Failure(
                new Error("Invitation.Forbidden", "Actor cannot resend this invitation."));
        }

        var token = tokens.Generate();
        var result = invitation.Reissue(
            token.TokenHash,
            DateTime.UtcNow.AddDays(options.Value.ValidityDays));
        if (result.IsFailure)
            return result;

        var tenant = await tenants.GetByIdAsync(invitation.TenantId, ct);

        await bus.PublishAsync(new InvitationCreatedIntegrationEvent
        {
            TenantId = invitation.TenantId,
            InvitationId = invitation.Id,
            Email = invitation.Email,
            ActorType = invitation.ActorType.ToString(),
            RawToken = token.RawToken,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            TenantName = tenant?.Name,
            InviterName = $"{actor.Name} {actor.LastName}",
            IsResend = true,
            CorrelationId = correlation.CorrelationId
        });

        await audit.AddAsync(
            AuthAuditLog.Record(
                invitation.TenantId, actor.Id, AuthAuditAction.InvitationResent, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                targetType: "Invitation", targetId: invitation.Id),
            ct);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
