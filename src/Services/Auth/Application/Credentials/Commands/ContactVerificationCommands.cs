using System.Net.Mail;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Credentials;
using Wolverine;

namespace TaxVision.Auth.Application.Credentials.Commands;

// ---------------------------------------------------------------------------
// Cambio de email (token a la dirección nueva)
// ---------------------------------------------------------------------------

public sealed record RequestEmailChangeCommand(Guid UserId, string NewEmail);

public static class RequestEmailChangeHandler
{
    private static readonly TimeSpan Validity = TimeSpan.FromHours(1);

    public static async Task<Result> Handle(
        RequestEmailChangeCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        var newEmail = command.NewEmail?.Trim().ToLowerInvariant() ?? string.Empty;
        if (!MailAddress.TryCreate(newEmail, out _))
            return Result.Failure(new Error("User.Email", "Email is invalid."));

        if (string.Equals(newEmail, user.Email, StringComparison.OrdinalIgnoreCase))
            return Result.Failure(new Error("User.Email", "New email must be different."));

        if (await users.EmailExistsAsync(user.TenantId, newEmail, ct))
        {
            return Result.Failure(new Error("User.EmailConflict", "Email is already registered in this tenant."));
        }

        var rawToken = tokens.GenerateToken();
        var verification = EmailVerificationToken.Create(
            user.TenantId,
            user.Id,
            newEmail,
            tokens.Hash(rawToken),
            Validity
        );
        await credentials.AddEmailVerificationAsync(verification, ct);

        await bus.PublishAsync(
            new EmailChangeRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                CurrentEmail = user.Email,
                NewEmail = newEmail,
                RawToken = rawToken,
                ExpiresAtUtc = verification.ExpiresAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.EmailChangeRequested,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ConfirmEmailChangeCommand(string Token);

public static class ConfirmEmailChangeHandler
{
    public static async Task<Result> Handle(
        ConfirmEmailChangeCommand command,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.InvalidVerificationToken", "Verification token is invalid or expired.");
        var now = DateTime.UtcNow;

        if (string.IsNullOrWhiteSpace(command.Token))
            return Result.Failure(invalid);

        var verification = await credentials.GetEmailVerificationByHashAsync(tokens.Hash(command.Token), ct);
        if (verification is null || !verification.IsUsable(now))
            return Result.Failure(invalid);

        var user = await users.GetByIdAsync(verification.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(invalid);

        if (await users.EmailExistsAsync(user.TenantId, verification.NewEmail, ct))
        {
            return Result.Failure(new Error("User.EmailConflict", "Email is already registered in this tenant."));
        }

        var previousEmail = user.Email;
        user.ChangeEmail(verification.NewEmail);
        verification.MarkUsed();

        await bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                AlertType = SecurityAlertType.EmailChanged,
                IpAddress = request.IpAddress,
                UserAgent = request.UserAgent,
                DetailsJson = $$"""{"previousEmail":"{{previousEmail}}"}""",
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.EmailChanged,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

// ---------------------------------------------------------------------------
// Verificación de teléfono (OTP por SMS)
// ---------------------------------------------------------------------------

public sealed record RequestPhoneVerificationCommand(Guid UserId, string PhoneNumber);

public static class RequestPhoneVerificationHandler
{
    private static readonly TimeSpan Validity = TimeSpan.FromMinutes(10);

    public static async Task<Result> Handle(
        RequestPhoneVerificationCommand command,
        IUserRepository users,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        ILoginThrottler throttler,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(new Error("User.NotFound", "User does not exist."));

        var phone = command.PhoneNumber?.Trim() ?? string.Empty;
        if (phone.Length is < 7 or > 20 || !phone.All(c => char.IsDigit(c) || c is '+' or '-' or ' '))
            return Result.Failure(new Error("User.Phone", "Phone number is invalid."));

        if (await throttler.IsOtpResendThrottledAsync(user.Id, ct))
            return Result.Failure(new Error("Auth.OtpThrottled", "Wait before requesting another code."));

        var code = tokens.GenerateNumericCode();
        var verification = PhoneVerificationToken.Create(user.TenantId, user.Id, phone, tokens.Hash(code), Validity);
        await credentials.AddPhoneVerificationAsync(verification, ct);
        await throttler.RegisterOtpSentAsync(user.Id, ct);

        await bus.PublishAsync(
            new MfaChallengeRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                Channel = "Sms",
                Destination = phone,
                Code = code,
                Purpose = "phone_verification",
                ExpiresAtUtc = verification.ExpiresAtUtc,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PhoneVerificationRequested,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}

public sealed record ConfirmPhoneVerificationCommand(Guid UserId, string Code);

public static class ConfirmPhoneVerificationHandler
{
    public static async Task<Result> Handle(
        ConfirmPhoneVerificationCommand command,
        ICredentialTokenRepository credentials,
        ISecureTokenService tokens,
        IUserRepository users,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var invalid = new Error("Auth.InvalidVerificationCode", "Verification code is invalid or expired.");
        var now = DateTime.UtcNow;

        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null || !user.IsActive)
            return Result.Failure(invalid);

        var verification = await credentials.GetActivePhoneVerificationAsync(user.Id, ct);
        if (verification is null || !verification.IsUsable(now))
            return Result.Failure(invalid);

        if (
            string.IsNullOrWhiteSpace(command.Code)
            || !string.Equals(tokens.Hash(command.Code), verification.CodeHash, StringComparison.Ordinal)
        )
        {
            verification.RegisterAttempt();
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure(invalid);
        }

        verification.MarkUsed();
        user.SetPhoneNumber(verification.PhoneNumber);

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.PhoneVerified,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
