using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.Common;

/// <summary>Resultado de verificar un segundo factor sin desafío previo.</summary>
public enum MfaCodeCheck
{
    Invalid,
    Totp,
    RecoveryCode,
}

/// <summary>
/// Segundo factor sin desafío: TOTP contra el secreto del método confirmado (stateless) o un recovery code.
/// No cubre OTP por Email/SMS, que necesitan un desafío enviado antes. Lo usan el handoff del login central
/// y la reautenticación.
/// </summary>
public static class MfaCodeVerifier
{
    /// <summary>¿El usuario tiene un TOTP confirmado? Si lo tiene, el step-up exige el código.</summary>
    public static async Task<bool> HasConfirmedTotpAsync(Guid userId, IMfaRepository mfa, CancellationToken ct) =>
        (await mfa.GetMethodsAsync(userId, ct)).Any(method => method.IsConfirmed && method.Type == MfaMethodType.Totp);

    public static async Task<MfaCodeCheck> VerifyAsync(
        Guid userId,
        string? code,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService tokens,
        CancellationToken ct
    )
    {
        if (string.IsNullOrWhiteSpace(code))
            return MfaCodeCheck.Invalid;

        var trimmed = code.Trim();
        var now = DateTime.UtcNow;

        var totpMethod = (await mfa.GetMethodsAsync(userId, ct)).FirstOrDefault(method =>
            method.IsConfirmed && method.Type == MfaMethodType.Totp
        );
        if (
            totpMethod?.SecretCiphertext is not null
            && protector.TryUnprotect(totpMethod.SecretCiphertext, out var secret, out _)
            && totp.ValidateCode(secret, trimmed, now)
        )
        {
            totpMethod.MarkUsed();
            return MfaCodeCheck.Totp;
        }

        var codeHash = tokens.Hash(trimmed);
        var recovery = (await mfa.GetRecoveryCodesAsync(userId, ct)).FirstOrDefault(recoveryCode =>
            recoveryCode.IsUsable && string.Equals(recoveryCode.CodeHash, codeHash, StringComparison.Ordinal)
        );
        if (recovery is not null)
        {
            recovery.MarkUsed();
            return MfaCodeCheck.RecoveryCode;
        }

        return MfaCodeCheck.Invalid;
    }
}
