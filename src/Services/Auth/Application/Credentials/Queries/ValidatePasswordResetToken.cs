using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Credentials.Queries;

public sealed record ValidatePasswordResetTokenQuery(string Token);

/// <summary>Dice si un enlace de reset todavía sirve, sin consumirlo ni contar intentos, para que la página avise
/// antes de pedir la contraseña nueva.</summary>
public static class ValidatePasswordResetTokenHandler
{
    public static async Task<Result> Handle(
        ValidatePasswordResetTokenQuery query,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        CancellationToken ct
    ) =>
        await PasswordResetLinks.FindUsableAsync(query.Token, credentials, tokens, users, DateTime.UtcNow, ct) is null
            ? Result.Failure(PasswordResetLinks.Invalid)
            : Result.Success();
}
