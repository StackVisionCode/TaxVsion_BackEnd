using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record RegisterCommand(Guid TenantId, string Email, string Password);
public sealed record UserResponse(Guid Id, Guid TenantId, string Email);

public static class RegisterHandler
{
    public static async Task<Result<UserResponse>> Handle(
        RegisterCommand command,
        IUserRepository users,
        IPasswordHasher hasher,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
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

        var result = User.Register(command.TenantId, email, hasher.Hash(command.Password));
        if (result.IsFailure)
            return Result.Failure<UserResponse>(result.Error);

        await users.AddAsync(result.Value, ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new UserResponse(result.Value.Id, result.Value.TenantId, result.Value.Email));
    }
}
