using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Users.IntegrationEvents;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record RegisterCommand(
    Guid TenantId,
    string Name,
    string LastName,
    string Email,
    string Password);

public sealed record UserResponse(Guid Id, Guid TenantId, string Name, string LastName, string Email);

public static class RegisterHandler
{
    public static async Task<Result<UserResponse>> Handle(
        RegisterCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();

        if (!await tenants.ExistsActiveAsync(command.TenantId, ct))
        {
            return Result.Failure<UserResponse>(
                new Error("Tenant.NotFound", "Tenant does not exist or is inactive."));
        }

        if (await users.EmailExistsAsync(email, ct))
        {
            return Result.Failure<UserResponse>(
                new Error("User.EmailConflict", "Email is already registered."));
        }

        if (string.IsNullOrWhiteSpace(command.Password) || command.Password.Length < 12)
        {
            return Result.Failure<UserResponse>(
                new Error("User.Password", "Password must contain at least 12 characters."));
        }

        var result = User.Register(
            command.TenantId,
            command.Name,
            command.LastName,
            email,
            hasher.Hash(command.Password));
        if (result.IsFailure)
            return Result.Failure<UserResponse>(result.Error);

        await users.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);
        // publish event
        await bus.PublishAsync(new UserRegisteredIntegrationEvent
        {
            UserId = result.Value.Id,
            TenantId = result.Value.TenantId,
            Email = result.Value.Email
        });

        return Result.Success(
            new UserResponse(
                result.Value.Id,
                result.Value.TenantId,
                result.Value.Name,
                result.Value.LastName,
                result.Value.Email));
    }
}
