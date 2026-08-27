using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.Sessions.Commands;

/// <summary>
/// Confirma el takeover de sesión única: canjea el vale emitido por el login (un solo uso), revoca
/// TODAS las sesiones anteriores del usuario y materializa la nueva. El vale ya prueba que el login
/// (password + MFA si tocaba) se resolvió — acá no se re-autentica.
/// </summary>
public sealed record TakeoverSessionCommand(Guid Ticket, string? DeviceName = null);

public static class TakeoverSessionHandler
{
    public static async Task<Result<LoginResponse>> Handle(
        TakeoverSessionCommand command,
        ISessionTakeoverTicketStore takeoverTickets,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
        IAuthSessionIssuer issuer,
        ISessionRepository sessions,
        IAccessTokenDenylist denylist,
        ISessionRevocationPublisher revocationPublisher,
        IAuthAuditWriter audit,
        IRequestContext request,
        ICorrelationContext correlation,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        // Vale inválido, expirado o ya usado son indistinguibles a propósito: el mensaje no revela cuál.
        var invalid = new Error("Auth.TakeoverInvalid", "The session takeover request is invalid or has expired.");

        var payload = await takeoverTickets.ConsumeAsync(command.Ticket, ct);
        if (payload is null)
            return Result.Failure<LoginResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(payload.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<LoginResponse>(invalid);

        // El vale es confiable, pero el usuario pudo desactivarse en la ventana; se revalida el estado.
        var user = await users.GetByIdAsync(payload.UserId, ct);
        if (user is null || user.TenantId != payload.TenantId || !user.IsActive)
            return Result.Failure<LoginResponse>(invalid);

        // Sesión única: aún no existe la nueva, así que se revocan TODAS las anteriores. Denylist cada
        // una (20 min cubre la vida máxima del JWT) y revocar en BD — mismo patrón que el cambio de
        // contraseña.
        var previous = await sessions.GetActiveSessionsByUserAsync(user.Id, ct);
        foreach (var session in previous)
            await denylist.DenySessionAsync(session.Id, TimeSpan.FromMinutes(20), ct);
        await sessions.RevokeAllForUserAsync(user.Id, "single_session_superseded", null, ct);

        var issued = await SessionEstablishment.IssueAsync(
            user,
            tenant,
            payload.AuthMethods,
            command.DeviceName ?? payload.DeviceName,
            roles,
            issuer,
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
                detailsJson: """{"sessionTakeover":true}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        // Post-commit: avisar en tiempo real a los dispositivos revocados (best-effort).
        foreach (var session in previous)
            await revocationPublisher.PublishRevokedAsync(
                user.TenantId,
                user.Id,
                session.Id,
                "single_session_superseded",
                ct
            );

        return Result.Success(
            LoginResponse.ForTokens(
                new AuthTokensResponse(issued.AccessToken, issued.RefreshToken, issued.ExpiresInSeconds),
                mfaSetupRequired: payload.MustEnrollMfa
            )
        );
    }
}
