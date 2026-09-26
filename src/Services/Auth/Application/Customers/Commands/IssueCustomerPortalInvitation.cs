using System.Net.Mail;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Invitations.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.Commands;

/// <summary>Customer pide el acceso al portal de un cliente con el email que Customer tiene registrado.</summary>
public sealed record IssueCustomerPortalInvitationCommand(
    Guid TenantId,
    Guid CustomerId,
    string Email,
    Guid? RequestedByUserId
);

public enum PortalInvitationOutcome
{
    /// <summary>Se creó la invitación y se envió el correo.</summary>
    Invited,

    /// <summary>Ya había una invitación pendiente: se regeneró el enlace y se reenvió el correo.</summary>
    Resent,

    /// <summary>El cliente ya tiene su cuenta de portal activa: no se envía nada.</summary>
    AlreadyActive,
}

public sealed record PortalInvitationResult(PortalInvitationOutcome Outcome, string Email, DateTime? ExpiresAtUtc);

/// <summary>
/// Acceso al portal de un cliente, con desenlace explícito. La cuenta de portal es independiente de una
/// cuenta Staff con el mismo email (un empleado puede ser cliente); lo que no puede haber es dos cuentas
/// de portal con el mismo email en la oficina. Pedirlo de nuevo con una invitación pendiente la reenvía.
/// </summary>
public static class IssueCustomerPortalInvitationHandler
{
    public static readonly Error EmailInUse = new(
        "Auth.PortalEmailInUse",
        "This email already has client portal access for another client in this office. Use a different email."
    );

    public static readonly Error AccessDeactivated = new(
        "Auth.PortalAccessDeactivated",
        "Portal access for this client is deactivated. Reactivate it instead of sending a new invitation."
    );

    public static async Task<Result<PortalInvitationResult>> Handle(
        IssueCustomerPortalInvitationCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IOptions<InvitationOptions> options,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var email = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!MailAddress.TryCreate(email, out _))
            return Result.Failure<PortalInvitationResult>(
                new Error("Invitation.Email", "Invitation email is invalid.")
            );

        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<PortalInvitationResult>(new Error("Tenant.Inactive", "Tenant is inactive."));

        var existing = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (existing is not null)
        {
            return existing.IsActive
                ? Result.Success(
                    new PortalInvitationResult(PortalInvitationOutcome.AlreadyActive, existing.Email, null)
                )
                : Result.Failure<PortalInvitationResult>(AccessDeactivated);
        }

        if (await users.EmailExistsAsync(command.TenantId, email, UserAccountKind.Portal, ct))
            return Result.Failure<PortalInvitationResult>(EmailInUse);

        var inviter = command.RequestedByUserId is { } requestedBy ? await users.GetByIdAsync(requestedBy, ct) : null;
        var inviterName = inviter is null ? null : $"{inviter.Name} {inviter.LastName}".Trim();
        var token = tokens.Generate();
        var expiresAtUtc = DateTime.UtcNow.AddDays(options.Value.ValidityDays);

        var pending = await invitations.GetPendingAsync(command.TenantId, email, UserAccountKind.Portal, ct);
        Invitation invitation;
        bool isResend;
        if (pending is not null)
        {
            if (pending.CustomerId != command.CustomerId)
                return Result.Failure<PortalInvitationResult>(EmailInUse);

            var reissue = pending.Reissue(token.TokenHash, expiresAtUtc);
            if (reissue.IsFailure)
                return Result.Failure<PortalInvitationResult>(reissue.Error);

            invitation = pending;
            isResend = true;
        }
        else
        {
            var created = Invitation.Create(
                tenantId: command.TenantId,
                email: email,
                actorType: UserActorType.CustomerPortal,
                customerId: command.CustomerId,
                invitedByUserId: command.RequestedByUserId,
                tokenHash: token.TokenHash,
                expiresAtUtc: expiresAtUtc
            );
            if (created.IsFailure)
                return Result.Failure<PortalInvitationResult>(created.Error);

            invitation = created.Value;
            invitation.MarkSent();
            await invitations.AddAsync(invitation, ct);
            isResend = false;
        }

        // El token viaja solo por el bus interno (outbox durable) hasta Notification.
        await bus.PublishAsync(
            new InvitationCreatedIntegrationEvent
            {
                TenantId = command.TenantId,
                InvitationId = invitation.Id,
                Email = email,
                ActorType = UserActorType.CustomerPortal.ToString(),
                RawToken = token.RawToken,
                ExpiresAtUtc = invitation.ExpiresAtUtc,
                TenantName = tenant.Name,
                TenantSubdomain = tenant.SubDomain,
                InviterName = inviterName,
                IsResend = isResend,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                command.TenantId,
                command.RequestedByUserId,
                isResend ? AuthAuditAction.InvitationResent : AuthAuditAction.InvitationCreated,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                targetType: "Invitation",
                targetId: invitation.Id,
                detailsJson: $$"""{"customerId":"{{command.CustomerId}}","accountKind":"portal"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new PortalInvitationResult(
                isResend ? PortalInvitationOutcome.Resent : PortalInvitationOutcome.Invited,
                email,
                invitation.ExpiresAtUtc
            )
        );
    }
}
