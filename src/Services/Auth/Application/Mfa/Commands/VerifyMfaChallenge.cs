using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Security;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.Mfa.Commands;

/// <summary>
/// Paso 2 del login: verifica el código TOTP/OTP o un recovery code contra el
/// desafío identificado por el login ticket, y emite la sesión y los tokens.
/// </summary>
public sealed record VerifyMfaChallengeCommand(
    string LoginTicket,
    string? Code,
    string? RecoveryCode,
    bool RememberDevice = false,
    string? DeviceName = null
);

/// <summary>
/// Respuesta del paso 2: tokens, o —si el usuario ya tenía una sesión activa— un vale de takeover de
/// sesión única para que el frontend confirme (mismo patrón que <see cref="LoginResponse"/>). Cuando
/// hace falta takeover se difiere el mint, y con él el "recordar dispositivo": el usuario re-verifica
/// el 2.º factor la próxima vez en el dispositivo nuevo.
/// </summary>
public sealed record MfaVerifyResponse(
    AuthTokensResponse? Tokens,
    bool TakeoverRequired = false,
    string? TakeoverTicket = null,
    int? TakeoverTicketExpiresInSeconds = null
)
{
    public static MfaVerifyResponse ForTokens(AuthTokensResponse tokens) => new(tokens);

    public static MfaVerifyResponse ForTakeover(string takeoverTicket, int ticketSeconds) =>
        new(
            null,
            TakeoverRequired: true,
            TakeoverTicket: takeoverTicket,
            TakeoverTicketExpiresInSeconds: ticketSeconds
        );
}

public static class VerifyMfaChallengeHandler
{
    public static async Task<Result<MfaVerifyResponse>> Handle(
        VerifyMfaChallengeCommand command,
        IMfaRepository mfa,
        ISecureTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        ITotpService totp,
        ISecretProtector protector,
        IAuthSessionIssuer issuer,
        ISessionRepository sessions,
        ISessionTakeoverTicketStore takeoverTickets,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.MfaInvalid", "MFA code is invalid or expired.");
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(command.LoginTicket))
            return Result.Failure<MfaVerifyResponse>(invalid);

        var challenge = await mfa.GetChallengeByTicketHashAsync(tokens.Hash(command.LoginTicket), ct);
        if (challenge is null || !challenge.IsUsable(now))
            return Result.Failure<MfaVerifyResponse>(invalid);

        var user = await users.GetByIdAsync(challenge.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<MfaVerifyResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<MfaVerifyResponse>(invalid);

        var verified = false;
        var usedRecoveryCode = false;
        string methodAmr = "otp";

        if (!string.IsNullOrWhiteSpace(command.RecoveryCode))
        {
            var codeHash = tokens.Hash(command.RecoveryCode.Trim());
            var recoveryCodes = await mfa.GetRecoveryCodesAsync(user.Id, ct);
            var match = recoveryCodes.FirstOrDefault(code =>
                code.IsUsable && string.Equals(code.CodeHash, codeHash, StringComparison.Ordinal)
            );
            if (match is not null)
            {
                match.MarkUsed();
                verified = true;
                usedRecoveryCode = true;
                methodAmr = "recovery";
            }
        }
        else if (challenge.OtpHash is not null)
        {
            verified =
                !string.IsNullOrWhiteSpace(command.Code)
                && string.Equals(tokens.Hash(command.Code.Trim()), challenge.OtpHash, StringComparison.Ordinal);
            methodAmr = "otp";
        }
        else if (challenge.MfaMethodId is Guid methodId)
        {
            var method = await mfa.GetMethodByIdAsync(methodId, ct);
            if (method?.SecretCiphertext is not null && !string.IsNullOrWhiteSpace(command.Code))
            {
                verified =
                    protector.TryUnprotect(method.SecretCiphertext, out var secret, out _)
                    && totp.ValidateCode(secret, command.Code.Trim(), now);
                if (verified)
                    method.MarkUsed();
            }
            methodAmr = "totp";
        }

        if (!verified)
        {
            challenge.RegisterAttempt();
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.MfaFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<MfaVerifyResponse>(invalid);
        }

        challenge.Consume();

        // Sesión única: si el usuario ya tiene una sesión activa, se difiere el mint (y con él el
        // "recordar dispositivo") hasta que confirme el takeover.
        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            tenant,
            ["pwd", methodAmr],
            command.DeviceName,
            mustEnrollMfa: false,
            roles,
            issuer,
            sessions,
            takeoverTickets,
            ct
        );

        if (outcome.TakeoverRequired)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.MfaSucceeded,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: $$"""{"method":"{{methodAmr}}","takeoverRequired":true}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(
                MfaVerifyResponse.ForTakeover(
                    outcome.TakeoverTicket!.Value.ToString(),
                    (int)LockoutPolicy.TakeoverTicketValidity.TotalSeconds
                )
            );
        }

        var issued = outcome.Tokens!;

        string? deviceToken = null;
        if (command.RememberDevice && !usedRecoveryCode)
        {
            var policy = await mfa.GetPolicyAsync(user.TenantId, ct);
            var trustedDays = policy?.TrustedDeviceDays ?? 30;
            deviceToken = tokens.GenerateToken();
            var device = TrustedDevice.Create(
                user.TenantId,
                user.Id,
                tokens.Hash(deviceToken),
                request.UserAgent,
                TimeSpan.FromDays(trustedDays)
            );
            await mfa.AddTrustedDeviceAsync(device, ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.TrustedDeviceAdded,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    targetType: "TrustedDevice",
                    targetId: device.Id
                ),
                ct
            );
        }

        if (usedRecoveryCode)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.RecoveryCodeUsed,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId
                ),
                ct
            );
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.MfaSucceeded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: $$"""{"method":"{{methodAmr}}"}"""
            ),
            ct
        );
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.LoginSucceeded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            MfaVerifyResponse.ForTokens(
                new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds, deviceToken)
            )
        );
    }
}
