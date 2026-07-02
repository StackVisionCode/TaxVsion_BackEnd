using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
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
    string? DeviceName = null);

public static class VerifyMfaChallengeHandler
{
    public static async Task<Result<AuthTokensResponse>> Handle(
        VerifyMfaChallengeCommand command,
        IMfaRepository mfa,
        ISecureTokenService tokens,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        ITotpService totp,
        ISecretProtector protector,
        IAuthSessionIssuer issuer,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var invalid = new Error("Auth.MfaInvalid", "MFA code is invalid or expired.");
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(command.LoginTicket))
            return Result.Failure<AuthTokensResponse>(invalid);

        var challenge = await mfa.GetChallengeByTicketHashAsync(
            tokens.Hash(command.LoginTicket), ct);
        if (challenge is null || !challenge.IsUsable(now))
            return Result.Failure<AuthTokensResponse>(invalid);

        var user = await users.GetByIdAsync(challenge.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(user.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<AuthTokensResponse>(invalid);

        var verified = false;
        var usedRecoveryCode = false;
        string methodAmr = "otp";

        if (!string.IsNullOrWhiteSpace(command.RecoveryCode))
        {
            var codeHash = tokens.Hash(command.RecoveryCode.Trim());
            var recoveryCodes = await mfa.GetRecoveryCodesAsync(user.Id, ct);
            var match = recoveryCodes.FirstOrDefault(
                code => code.IsUsable &&
                        string.Equals(code.CodeHash, codeHash, StringComparison.Ordinal));
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
                !string.IsNullOrWhiteSpace(command.Code) &&
                string.Equals(tokens.Hash(command.Code.Trim()), challenge.OtpHash, StringComparison.Ordinal);
            methodAmr = "otp";
        }
        else if (challenge.MfaMethodId is Guid methodId)
        {
            var method = await mfa.GetMethodByIdAsync(methodId, ct);
            if (method?.SecretCiphertext is not null && !string.IsNullOrWhiteSpace(command.Code))
            {
                var secret = protector.Unprotect(method.SecretCiphertext);
                verified = secret is not null && totp.ValidateCode(secret, command.Code.Trim(), now);
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
                    user.TenantId, user.Id, AuthAuditAction.MfaFailed, false,
                    request.IpAddress, request.UserAgent, correlation.CorrelationId),
                ct);
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<AuthTokensResponse>(invalid);
        }

        challenge.Consume();

        var (roleNames, permissions) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);

        var issued = await issuer.StartSessionAsync(
            user, timeZone, roleNames, permissions, ["pwd", methodAmr], command.DeviceName, ct);

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
                TimeSpan.FromDays(trustedDays));
            await mfa.AddTrustedDeviceAsync(device, ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId, user.Id, AuthAuditAction.TrustedDeviceAdded, true,
                    request.IpAddress, request.UserAgent, correlation.CorrelationId,
                    targetType: "TrustedDevice", targetId: device.Id),
                ct);
        }

        if (usedRecoveryCode)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId, user.Id, AuthAuditAction.RecoveryCodeUsed, true,
                    request.IpAddress, request.UserAgent, correlation.CorrelationId),
                ct);
        }

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.MfaSucceeded, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                detailsJson: $$"""{"method":"{{methodAmr}}"}"""),
            ct);
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.LoginSucceeded, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new AuthTokensResponse(
            issued.AccessToken,
            issued.RefreshToken,
            issued.ExpiresInSeconds,
            deviceToken));
    }
}
