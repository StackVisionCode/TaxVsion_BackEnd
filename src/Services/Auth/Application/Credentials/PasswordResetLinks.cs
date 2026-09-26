using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Credentials;

/// <summary>Enlace de reset que todavía sirve junto a su cuenta activa. Lo comparten el reset y la validación
/// previa de la página, para que ambos decidan lo mismo.</summary>
internal static class PasswordResetLinks
{
    public static readonly Error Invalid = new("Auth.InvalidResetToken", "Reset token is invalid or expired.");

    public static async Task<(PasswordResetToken Link, User User)?> FindUsableAsync(
        string? rawToken,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        DateTime utcNow,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(rawToken))
            return null;

        var link = await credentials.GetPasswordResetByHashAsync(tokens.Hash(rawToken), ct);
        if (link is null || !link.IsUsable(utcNow))
            return null;

        var user = await users.GetByIdAsync(link.UserId, ct);
        return user is { IsActive: true } ? (link, user) : null;
    }
}
