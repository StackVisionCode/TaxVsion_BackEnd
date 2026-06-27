using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record RefreshAccessTokenCommand(string RefreshToken);

public static class RefreshAccessTokenHandler
{
    public static async Task<Result<LoginResponse>> Handle(
        RefreshAccessTokenCommand command,
        IRefreshTokenService refreshTokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IJwtTokenGenerator jwt,
        CancellationToken ct)
    {
        var userId = await refreshTokens.GetActiveUserIdAsync(command.RefreshToken, ct);
        if (userId is null)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        var user = await users.GetByIdAsync(userId.Value, ct);
        if (user is null || !user.IsActive)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        if (!await tenants.ExistsActiveAsync(user.TenantId, ct))
        {
            return Result.Failure<LoginResponse>(
                new Error("Tenant.Inactive", "Tenant is inactive."));
        }

        var replacement = await refreshTokens.RotateAsync(command.RefreshToken, ct);
        if (replacement is null)
        {
            return Result.Failure<LoginResponse>(
                new Error("Auth.InvalidRefreshToken", "Refresh token is invalid or expired."));
        }

        return Result.Success(new LoginResponse(
            jwt.Generate(user),
            replacement));
    }
}

public sealed record RevokeRefreshTokenCommand(string RefreshToken);

public static class RevokeRefreshTokenHandler
{
    public static async Task<Result> Handle(
        RevokeRefreshTokenCommand command,
        IRefreshTokenService refreshTokens,
        CancellationToken ct)
    {
        await refreshTokens.RevokeAsync(command.RefreshToken, ct);
        return Result.Success();
    }
}
