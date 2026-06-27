using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record ActivateTenantAdminCommand(
    Guid TenantId,
    string ActivationToken,
    string Name,
    string LastName,
    string Password);

public static class ActivateTenantAdminHandler
{
    public static async Task<Result<UserResponse>> Handle(
        ActivateTenantAdminCommand command,
        ITenantRegistry tenants,
        IUserRepository users,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct)
    {
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null)
        {
            return Result.Failure<UserResponse>(
                new Error("Tenant.NotFound", "Tenant does not exist."));
        }

        if (!tenant.IsActive)
        {
            return Result.Failure<UserResponse>(
                new Error("Tenant.Inactive", "Tenant is inactive."));
        }

        if (!tenant.MatchesAdminInvitation(command.ActivationToken))
        {
            return Result.Failure<UserResponse>(
                new Error("Auth.InvalidInvitation", "Admin activation token is invalid."));
        }

        if (tenant.AdminUserId is Guid existingAdminId)
        {
            var existingAdmin = await users.GetByIdAsync(existingAdminId, ct);
            if (existingAdmin is not null)
                return Result.Success(ToResponse(existingAdmin));
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 12)
        {
            return Result.Failure<UserResponse>(
                new Error("User.Password", "Password must contain at least 12 characters."));
        }

        var adminEmail = tenant.AdminEmail!;
        if (await users.EmailExistsAsync(command.TenantId, adminEmail, ct))
        {
            return Result.Failure<UserResponse>(
                new Error("User.EmailConflict", "Admin email is already registered."));
        }

        var result = User.Register(
            command.TenantId,
            command.Name,
            command.LastName,
            adminEmail,
            hasher.Hash(command.Password));

        if (result.IsFailure)
            return Result.Failure<UserResponse>(result.Error);

        result.Value.AssignRole("TenantAdmin");
        await users.AddAsync(result.Value, ct);
        tenant.MarkAdminInvitationConsumed(result.Value.Id);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(new UserRegisteredIntegrationEvent
        {
            UserId = result.Value.Id,
            TenantId = result.Value.TenantId,
            Email = result.Value.Email,
            CorrelationId = correlation.CorrelationId
        });

        return Result.Success(ToResponse(result.Value));
    }

    private static UserResponse ToResponse(User user) =>
        new(user.Id, user.TenantId, user.Name, user.LastName, user.Email);
}
