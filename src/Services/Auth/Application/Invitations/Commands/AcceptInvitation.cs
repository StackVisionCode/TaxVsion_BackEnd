using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Common;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Domain.Invitations;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Invitations.Commands;

public sealed record AcceptInvitationCommand(
    string InvitationToken,
    string Name,
    string LastName,
    string Password);

public static class AcceptInvitationHandler
{
    public static async Task<Result<UserResponse>> Handle(
        AcceptInvitationCommand command,
        IInvitationRepository invitations,
        IInvitationTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher passwordHasher,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var tokenHash = tokens.Hash(command.InvitationToken);
        var invitation = await invitations.GetByTokenHashAsync(tokenHash, ct);
        if (invitation is null || !invitation.MatchesTokenHash(tokenHash))
        {
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired."));
        }

        if (invitation.Status == InvitationStatus.Accepted &&
            invitation.AcceptedByUserId is Guid existingUserId)
        {
            var existing = await users.GetByIdAsync(existingUserId, ct);
            if (existing is not null)
                return Result.Success(ToResponse(existing));
        }

        var now = DateTime.UtcNow;
        if (invitation.MarkExpired(now))
        {
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired."));
        }

        if (invitation.Status != InvitationStatus.Pending)
        {
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Invitation is invalid or expired."));
        }

        var tenant = await tenants.GetByIdAsync(invitation.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            return Result.Failure<UserResponse>(
                new Error("Tenant.Inactive", "Tenant does not exist or is inactive."));
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 12)
        {
            return Result.Failure<UserResponse>(
                new Error("User.Password", "Password must contain at least 12 characters."));
        }

        if (await users.EmailExistsAsync(invitation.TenantId, invitation.Email, ct))
        {
            return Result.Failure<UserResponse>(
                new Error("User.EmailConflict", "Email is already registered in this tenant."));
        }

        var userResult = User.Register(
            invitation.TenantId,
            command.Name,
            command.LastName,
            invitation.Email,
            passwordHasher.Hash(command.Password),
            invitation.ActorType,
            invitation.CustomerId);
        if (userResult.IsFailure)
            return Result.Failure<UserResponse>(userResult.Error);

        var acceptResult = invitation.Accept(userResult.Value.Id, now);
        if (acceptResult.IsFailure)
            return Result.Failure<UserResponse>(acceptResult.Error);

        await users.AddAsync(userResult.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new UserRegisteredIntegrationEvent
        {
            UserId = userResult.Value.Id,
            TenantId = userResult.Value.TenantId,
            Email = userResult.Value.Email,
            ActorType = userResult.Value.ActorType.ToString(),
            CustomerId = userResult.Value.CustomerId,
            CorrelationId = correlation.CorrelationId
        });

        return Result.Success(ToResponse(userResult.Value));
    }

    private static UserResponse ToResponse(User user) =>
        new(
            user.Id,
            user.TenantId,
            user.Name,
            user.LastName,
            user.Email,
            user.ActorType,
            user.CustomerId);
}
