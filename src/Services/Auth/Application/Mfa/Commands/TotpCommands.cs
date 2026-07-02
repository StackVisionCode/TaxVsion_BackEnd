using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;

namespace TaxVision.Auth.Application.Mfa.Commands;

// ---------------------------------------------------------------------------
// Enrolamiento TOTP (authenticator app)
// ---------------------------------------------------------------------------

public sealed record SetupTotpCommand(Guid UserId);

public sealed record SetupTotpResponse(string Secret, string OtpAuthUri);

public static class SetupTotpHandler
{
    public static async Task<Result<SetupTotpResponse>> Handle(
        SetupTotpCommand command,
        IUserRepository users,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<SetupTotpResponse>(new Error("User.NotFound", "User does not exist."));

        var secret = totp.GenerateSecret();
        var ciphertext = protector.Protect(secret);

        var existing = await mfa.GetMethodAsync(user.Id, MfaMethodType.Totp, ct);
        if (existing is not null)
        {
            if (existing.IsConfirmed)
            {
                return Result.Failure<SetupTotpResponse>(
                    new Error("Mfa.AlreadyEnabled", "TOTP is already configured. Disable it first."));
            }

            existing.ReplaceSecret(ciphertext);
        }
        else
        {
            var methodResult = MfaMethod.Create(
                user.TenantId, user.Id, MfaMethodType.Totp, ciphertext, destination: null);
            if (methodResult.IsFailure)
                return Result.Failure<SetupTotpResponse>(methodResult.Error);
            await mfa.AddMethodAsync(methodResult.Value, ct);
        }

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new SetupTotpResponse(
            secret,
            totp.BuildOtpAuthUri(user.Email, secret, "TaxVision")));
    }
}

public sealed record ConfirmTotpCommand(Guid UserId, string Code);

public sealed record ConfirmTotpResponse(IReadOnlyList<string> RecoveryCodes);

public static class ConfirmTotpHandler
{
    public const int RecoveryCodeCount = 10;

    public static async Task<Result<ConfirmTotpResponse>> Handle(
        ConfirmTotpCommand command,
        IUserRepository users,
        IMfaRepository mfa,
        ITotpService totp,
        ISecretProtector protector,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure<ConfirmTotpResponse>(new Error("User.NotFound", "User does not exist."));

        var method = await mfa.GetMethodAsync(user.Id, MfaMethodType.Totp, ct);
        if (method?.SecretCiphertext is null)
            return Result.Failure<ConfirmTotpResponse>(new Error("Mfa.NotSetUp", "TOTP setup was not started."));

        var secret = protector.Unprotect(method.SecretCiphertext);
        if (secret is null || !totp.ValidateCode(secret, command.Code, DateTime.UtcNow))
        {
            return Result.Failure<ConfirmTotpResponse>(
                new Error("Auth.MfaInvalid", "Verification code is invalid."));
        }

        method.Confirm();
        method.MarkPreferred(true);
        user.EnableMfa();

        // Recovery codes: se muestran una única vez y se guardan hasheados.
        var existingCodes = await mfa.GetRecoveryCodesAsync(user.Id, ct);
        mfa.RemoveRecoveryCodes(existingCodes);

        var rawCodes = new List<string>(RecoveryCodeCount);
        var entities = new List<RecoveryCode>(RecoveryCodeCount);
        for (var i = 0; i < RecoveryCodeCount; i++)
        {
            var raw = tokens.GenerateToken(6);
            rawCodes.Add(raw);
            entities.Add(RecoveryCode.Create(user.TenantId, user.Id, tokens.Hash(raw)));
        }
        await mfa.AddRecoveryCodesAsync(entities, ct);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId, user.Id, AuthAuditAction.MfaEnabled, true,
                request.IpAddress, request.UserAgent, correlation.CorrelationId,
                detailsJson: """{"method":"Totp"}"""),
            ct);
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ConfirmTotpResponse(rawCodes));
    }
}
