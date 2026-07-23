using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Domain.Audit;
using TaxVision.Auth.Domain.Mfa;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Users.Commands;

public sealed record LoginCommand(
    Guid TenantId,
    string Email,
    string Password,
    string? DeviceName = null,
    string? DeviceToken = null
);

public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    string? DeviceToken = null
);

/// <summary>
/// Respuesta polimórfica del login: tokens directos, o desafío MFA pendiente
/// (loginTicket + métodos disponibles), o aviso de enrolamiento MFA requerido.
/// </summary>
public sealed record LoginResponse(
    bool MfaRequired,
    bool MfaSetupRequired,
    AuthTokensResponse? Tokens,
    string? LoginTicket,
    string[]? MfaMethods,
    int? TicketExpiresInSeconds
)
{
    public static LoginResponse ForTokens(AuthTokensResponse tokens, bool mfaSetupRequired = false) =>
        new(false, mfaSetupRequired, tokens, null, null, null);

    public static LoginResponse ForMfaChallenge(string loginTicket, string[] methods, int ticketSeconds) =>
        new(true, false, null, loginTicket, methods, ticketSeconds);
}

public static class LockoutPolicy
{
    public const int MaxFailedAttempts = 10;
    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan MfaTicketValidity = TimeSpan.FromMinutes(5);
}

public static class LoginHandler
{
    public static async Task<Result<LoginResponse>> Handle(
        LoginCommand command,
        IUserRepository users,
        ITenantRegistry tenants,
        IPasswordHasher hasher,
        IRoleRepository roles,
        IMfaRepository mfa,
        IAuthSessionIssuer issuer,
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
        var invalidCredentials = new Error("Auth.Invalid", "Invalid credentials.");
        var now = DateTime.UtcNow;

        // 1. Throttle por IP (defensa complementaria al rate limiting del Gateway).
        var retryAfter = await throttler.GetIpRetryAfterAsync(request.IpAddress, ct);
        if (retryAfter is not null)
        {
            return Result.Failure<LoginResponse>(new Error("Auth.LockedOut", "Too many attempts. Try again later."));
        }

        // 2. Tenant. Respuesta genérica hacia el anónimo (anti-enumeración);
        //    el motivo real queda solo en auditoría.
        var tenant = await tenants.GetByIdAsync(command.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
        {
            await throttler.RegisterFailureAsync(request.IpAddress, ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    command.TenantId,
                    null,
                    AuthAuditAction.LoginFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: """{"reason":"tenant_inactive_or_missing"}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<LoginResponse>(invalidCredentials);
        }

        var email = command.Email.Trim().ToLowerInvariant();
        var user = await users.GetByEmailAsync(command.TenantId, email, ct);

        if (user is null)
        {
            await throttler.RegisterFailureAsync(request.IpAddress, ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    command.TenantId,
                    null,
                    AuthAuditAction.LoginFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: """{"reason":"unknown_email"}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<LoginResponse>(invalidCredentials);
        }

        // 3. Lockout autoritativo por cuenta.
        if (user.IsLockedOut(now))
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.LoginLockedOut,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<LoginResponse>(
                new Error("Auth.LockedOut", "Account is temporarily locked. Try again later.")
            );
        }

        // 4. Verificación de contraseña.
        if (!hasher.Verify(command.Password, user.PasswordHash))
        {
            user.RegisterFailedLogin(now, LockoutPolicy.MaxFailedAttempts, LockoutPolicy.LockoutDuration);
            await throttler.RegisterFailureAsync(request.IpAddress, ct);
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.LoginFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: """{"reason":"bad_password"}"""
                ),
                ct
            );

            if (user.IsLockedOut(now))
            {
                await bus.PublishAsync(
                    new SecurityAlertIntegrationEvent
                    {
                        TenantId = user.TenantId,
                        UserId = user.Id,
                        AlertType = SecurityAlertType.AccountLockedOut,
                        IpAddress = request.IpAddress,
                        UserAgent = request.UserAgent,
                        CorrelationId = correlation.CorrelationId,
                    }
                );
            }

            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<LoginResponse>(invalidCredentials);
        }

        if (!user.IsActive)
        {
            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.LoginFailed,
                    false,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: """{"reason":"user_inactive"}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Failure<LoginResponse>(invalidCredentials);
        }

        user.RegisterSuccessfulLogin();

        var (roleNames, _) = await UserAccessResolver.ResolveAsync(user, roles, ct);
        var timeZone = UserAccessResolver.EffectiveTimeZone(user, tenant);

        // 5. Evaluación MFA (política del tenant + preferencia del usuario).
        var policy = await mfa.GetPolicyAsync(user.TenantId, ct);
        var mfaRequired =
            user.MfaEnabled
            || (
                policy?.RequiresFor(user.ActorType)
                ?? user.ActorType is UserActorType.TenantAdmin or UserActorType.PlatformAdmin
            );

        if (mfaRequired)
        {
            var confirmedMethods = (await mfa.GetMethodsAsync(user.Id, ct))
                .Where(method => method.IsConfirmed)
                .ToList();

            if (confirmedMethods.Count == 0)
            {
                // Sin método registrado: se permite el acceso con la bandera de
                // enrolamiento obligatorio para que el frontend fuerce el setup.
                var setupTokens = await issuer.StartSessionAsync(
                    user,
                    timeZone,
                    roleNames,
                    ["pwd"],
                    command.DeviceName,
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
                        correlation.CorrelationId,
                        detailsJson: """{"mfaSetupRequired":true}"""
                    ),
                    ct
                );
                await unitOfWork.SaveChangesAsync(ct);
                return Result.Success(
                    LoginResponse.ForTokens(
                        new AuthTokensResponse(
                            setupTokens.AccessToken,
                            setupTokens.RefreshToken,
                            setupTokens.ExpiresInSeconds
                        ),
                        mfaSetupRequired: true
                    )
                );
            }

            // Dispositivo confiable: omite el segundo factor.
            if (!string.IsNullOrWhiteSpace(command.DeviceToken))
            {
                var device = await mfa.GetTrustedDeviceByHashAsync(tokens.Hash(command.DeviceToken), ct);
                if (device is { IsActive: true } && device.UserId == user.Id)
                {
                    var trustedTokens = await issuer.StartSessionAsync(
                        user,
                        timeZone,
                        roleNames,
                        ["pwd"],
                        command.DeviceName,
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
                            correlation.CorrelationId,
                            detailsJson: """{"trustedDevice":true}"""
                        ),
                        ct
                    );
                    await unitOfWork.SaveChangesAsync(ct);
                    return Result.Success(
                        LoginResponse.ForTokens(
                            new AuthTokensResponse(
                                trustedTokens.AccessToken,
                                trustedTokens.RefreshToken,
                                trustedTokens.ExpiresInSeconds
                            )
                        )
                    );
                }
            }

            // Crear desafío MFA (paso 2 pendiente).
            var challengeMethod =
                confirmedMethods.FirstOrDefault(method => method.IsPreferred)
                ?? confirmedMethods.FirstOrDefault(method => method.Type == MfaMethodType.Totp)
                ?? confirmedMethods[0];

            var loginTicket = tokens.GenerateToken();
            string? otpHash = null;

            if (challengeMethod.Type is MfaMethodType.Email or MfaMethodType.Sms)
            {
                var otpCode = tokens.GenerateNumericCode();
                otpHash = tokens.Hash(otpCode);
                await bus.PublishAsync(
                    new MfaChallengeRequestedIntegrationEvent
                    {
                        TenantId = user.TenantId,
                        UserId = user.Id,
                        Channel = challengeMethod.Type.ToString(),
                        Destination = challengeMethod.Destination ?? user.Email,
                        Code = otpCode,
                        Purpose = "login",
                        ExpiresAtUtc = now.Add(LockoutPolicy.MfaTicketValidity),
                        CorrelationId = correlation.CorrelationId,
                    }
                );
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

            await audit.AddAsync(
                AuthAuditLog.Record(
                    user.TenantId,
                    user.Id,
                    AuthAuditAction.MfaChallengeSent,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: $$"""{"method":"{{challengeMethod.Type}}"}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);

            return Result.Success(
                LoginResponse.ForMfaChallenge(
                    loginTicket,
                    confirmedMethods.Select(method => method.Type.ToString()).Distinct().ToArray(),
                    (int)LockoutPolicy.MfaTicketValidity.TotalSeconds
                )
            );
        }

        // 6. Sin MFA: emitir sesión y tokens directamente.
        var issued = await issuer.StartSessionAsync(user, timeZone, roleNames, ["pwd"], command.DeviceName, ct);

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
            LoginResponse.ForTokens(
                new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds)
            )
        );
    }
}
