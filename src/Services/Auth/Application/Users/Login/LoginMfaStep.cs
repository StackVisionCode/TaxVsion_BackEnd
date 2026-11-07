using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Users.Login;

/// <summary>Tercer paso: política del tenant y preferencia del usuario sobre el segundo factor.</summary>
public sealed class LoginMfaStep(
    IMfaRepository mfa,
    ISecureTokenService tokens,
    LoginSessionStep sessionStep,
    AuthAuditTrail audit,
    IOptions<MfaOptions> mfaOptions
)
{
    /// <summary>Devuelve cómo termina el login, o null si no hace falta segundo factor.</summary>
    public async Task<LoginDecision?> DecideAsync(
        LoginAttempt attempt,
        string? deviceToken,
        DateTime now,
        CancellationToken ct
    )
    {
        var user = attempt.User;
        if (!await MfaRequirement.EvaluateAsync(user, mfa, ct, mfaOptions.Value.Enforced))
            return null;

        var confirmedMethods = (await mfa.GetMethodsAsync(user.Id, ct)).Where(method => method.IsConfirmed).ToList();

        // Sin método registrado: se deja entrar con la bandera de enrolamiento obligatorio para que el frontend
        // fuerce el setup. Sesión única: el flag viaja en el vale del takeover y vuelve al confirmar.
        if (confirmedMethods.Count == 0)
            return await sessionStep.OpenAsync(attempt, LoginSessionEntry.MfaSetupPending, ct);

        // Dispositivo confiable: omite el 2.º factor, pero igual pasa por el gate de sesión única.
        if (await IsTrustedDeviceAsync(user, deviceToken, ct))
            return await sessionStep.OpenAsync(attempt, LoginSessionEntry.TrustedDevice, ct);

        return await ChallengeAsync(user, confirmedMethods, now, ct);
    }

    private async Task<bool> IsTrustedDeviceAsync(User user, string? deviceToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(deviceToken))
            return false;
        var device = await mfa.GetTrustedDeviceByHashAsync(tokens.Hash(deviceToken), ct);
        return device is { IsActive: true } && device.UserId == user.Id;
    }

    // Crea el desafío (paso 2 pendiente). El OTP se publica recién después de guardarlo.
    private async Task<LoginDecision> ChallengeAsync(
        User user,
        IReadOnlyList<MfaMethod> confirmedMethods,
        DateTime now,
        CancellationToken ct
    )
    {
        var challengeMethod =
            confirmedMethods.FirstOrDefault(method => method.IsPreferred)
            ?? confirmedMethods.FirstOrDefault(method => method.Type == MfaMethodType.Totp)
            ?? confirmedMethods[0];

        var loginTicket = tokens.GenerateToken();
        string? otpHash = null;
        MfaChallengeRequestedIntegrationEvent? otpRequest = null;

        if (challengeMethod.Type is MfaMethodType.Email or MfaMethodType.Sms)
        {
            var otpCode = tokens.GenerateNumericCode();
            otpHash = tokens.Hash(otpCode);
            otpRequest = new MfaChallengeRequestedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                Channel = challengeMethod.Type.ToString(),
                Destination = challengeMethod.Destination ?? user.Email,
                Code = otpCode,
                Purpose = "login",
                ExpiresAtUtc = now.Add(LockoutPolicy.MfaTicketValidity),
                CorrelationId = audit.CorrelationId,
            };
        }

        var challenge = MfaChallenge.Create(
            user.TenantId,
            user.Id,
            challengeMethod.Id,
            tokens.Hash(loginTicket),
            otpHash,
            LockoutPolicy.MfaTicketValidity
        );
        await mfa.AddChallengeAsync(challenge, ct);

        await audit.RecordAsync(
            user.TenantId,
            user.Id,
            AuthAuditAction.MfaChallengeSent,
            true,
            ct,
            detailsJson: $$"""{"method":"{{challengeMethod.Type}}"}"""
        );

        return LoginDecision.Answered(
            LoginResponse.ForMfaChallenge(
                loginTicket,
                confirmedMethods.Select(method => method.Type.ToString()).Distinct().ToArray(),
                (int)LockoutPolicy.MfaTicketValidity.TotalSeconds
            ),
            otpRequest
        );
    }
}
