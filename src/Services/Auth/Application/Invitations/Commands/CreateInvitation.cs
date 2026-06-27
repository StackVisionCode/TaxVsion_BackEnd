using System.Net.Mail;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Invitations.Commands;

public sealed record CreateInvitationCommand(
    Guid InvitedByUserId,
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId);

public sealed record CreateInvitationResponse(
    Guid InvitationId,
    Guid TenantId,
    string Email,
    UserActorType ActorType,
    Guid? CustomerId,
    string InvitationToken,
    DateTime ExpiresAtUtc);

public static class CreateInvitationHandler
{
    private static readonly TimeSpan InvitationValidity = TimeSpan.FromDays(7);

    public static async Task<Result<CreateInvitationResponse>> Handle(
        CreateInvitationCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var inviter = await users.GetByIdAsync(command.InvitedByUserId, ct);
        if (inviter is null || !inviter.IsActive)
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Invitation.Forbidden", "Inviter is not authorized."));
        }

        if (!CanInvite(inviter, command))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error(
                    "Invitation.Forbidden",
                    "This actor cannot create the requested invitation."));
        }

        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Tenant.Inactive", "Target tenant does not exist or is inactive."));
        }

        var normalizedEmail = command.Email?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!MailAddress.TryCreate(normalizedEmail, out var parsedEmail) ||
            !string.Equals(
                parsedEmail.Address,
                normalizedEmail,
                StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("Invitation.Email", "Invitation email is invalid."));
        }

        if (await users.EmailExistsAsync(command.TenantId, normalizedEmail, ct))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error("User.EmailConflict", "Email is already registered in this tenant."));
        }

        if (await invitations.HasPendingAsync(command.TenantId, normalizedEmail, ct))
        {
            return Result.Failure<CreateInvitationResponse>(
                new Error(
                    "Invitation.PendingConflict",
                    "A pending invitation already exists for this email and tenant."));
        }

        var token = tokens.Generate();
        var expiresAtUtc = DateTime.UtcNow.Add(InvitationValidity);
        var result = Invitation.Create(
            command.TenantId,
            normalizedEmail,
            command.ActorType,
            command.CustomerId,
            inviter.Id,
            token.TokenHash,
            expiresAtUtc);
        if (result.IsFailure)
            return Result.Failure<CreateInvitationResponse>(result.Error);

        await invitations.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new CreateInvitationResponse(
                result.Value.Id,
                result.Value.TenantId,
                result.Value.Email,
                result.Value.ActorType,
                result.Value.CustomerId,
                token.RawToken,
                result.Value.ExpiresAtUtc));
    }

    private static bool CanInvite(User inviter, CreateInvitationCommand command) =>
        inviter.ActorType switch
        {
            UserActorType.PlatformAdmin =>
                command.ActorType == UserActorType.PlatformAdmin
                    ? command.TenantId == PlatformTenant.Id
                    : command.ActorType == UserActorType.TenantAdmin &&
                      command.TenantId != PlatformTenant.Id,

            UserActorType.TenantAdmin =>
                inviter.TenantId == command.TenantId &&
                command.TenantId != PlatformTenant.Id &&
                command.ActorType is
                    UserActorType.TenantAdmin or
                    UserActorType.TenantEmployee or
                    UserActorType.CustomerPortal,

            _ => false
        };
}
