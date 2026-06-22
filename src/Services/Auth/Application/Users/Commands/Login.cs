using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record LoginCommand(string Email, string Password);
public sealed record LoginResponse(string AccessToken, string RefreshToken);

public static class LoginHandler
{
    public static async Task<Result<LoginResponse>> Handle(
        LoginCommand command,
        IUserRepository users,
        IPasswordHasher hasher,
        IJwtTokenGenerator jwt,
        IRefreshTokenService refreshTokens,
        CancellationToken ct)
    {
        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(email, ct);

        if (user is null || !hasher.Verify(command.Password, user.PasswordHash))
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.Invalid", "Invalid credentials."));
        }

        if (!user.IsActive)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.Inactive", "User is inactive."));
        }

        var accessToken = jwt.Generate(user, user.Roles);
        var refreshToken = await refreshTokens.IssueAsync(user.Id, ct);

        return Result.Success(new LoginResponse(accessToken, refreshToken));
    }
}
