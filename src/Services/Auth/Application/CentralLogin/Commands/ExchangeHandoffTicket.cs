using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Users.Commands;
using TaxVision.Auth.Domain.Audit;

namespace TaxVision.Auth.Application.CentralLogin.Commands;

public sealed record ExchangeHandoffTicketCommand(Guid Ticket, string? DeviceName = null);

/// <summary>
/// Tokens de la sesión recién materializada + <see cref="MfaSetupRequired"/>: cuando el usuario debe
/// enrolar MFA (política sin método), el frontend usa el flag para forzar el setup, igual que el
/// desenlace del login directo.
/// </summary>
public sealed record HandoffSessionResponse(
    string? AccessToken,
    string? RefreshToken,
    int ExpiresInSeconds,
    bool MfaSetupRequired,
    bool TakeoverRequired = false,
    string? TakeoverTicket = null,
    int? TakeoverTicketExpiresInSeconds = null
)
{
    public static HandoffSessionResponse ForTokens(
        string accessToken,
        string refreshToken,
        int expiresInSeconds,
        bool mfaSetupRequired
    ) => new(accessToken, refreshToken, expiresInSeconds, mfaSetupRequired);

    // Sesión única: el usuario ya tenía una sesión activa. No hay tokens todavía — el portal muestra
    // el interstitial y canjea el vale en POST /auth/session/takeover si confirma.
    public static HandoffSessionResponse ForTakeover(string takeoverTicket, int ticketSeconds, bool mfaSetupRequired) =>
        new(
            null,
            null,
            0,
            mfaSetupRequired,
            TakeoverRequired: true,
            TakeoverTicket: takeoverTicket,
            TakeoverTicketExpiresInSeconds: ticketSeconds
        );
}

/// <summary>
/// Canje del vale de handoff en el host de la oficina: consume el vale (un solo uso), verifica que
/// tenant y usuario siguen activos, y materializa la sesión. El vale ya prueba que el password (y el
/// MFA si tocaba) se resolvieron en el host central — acá no se re-autentica, solo se emiten tokens.
/// </summary>
public static class ExchangeHandoffTicketHandler
{
    public static async Task<Result<HandoffSessionResponse>> Handle(
        ExchangeHandoffTicketCommand command,
        IHandoffTicketStore tickets,
        IUserRepository users,
        ITenantRegistry tenants,
        IRoleRepository roles,
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
        // Vale inválido, expirado o ya usado son indistinguibles a propósito: el mensaje no revela cuál.
        var invalid = new Error("Auth.HandoffInvalid", "The sign-in link is invalid or has expired.");

        var payload = await tickets.ConsumeAsync(command.Ticket, ct);
        if (payload is null)
            return Result.Failure<HandoffSessionResponse>(invalid);

        var tenant = await tenants.GetByIdAsync(payload.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return Result.Failure<HandoffSessionResponse>(invalid);

        // El vale es confiable, pero el usuario pudo desactivarse en la ventana; se revalida el estado.
        var user = await users.GetByIdAsync(payload.UserId, ct);
        if (user is null || user.TenantId != payload.TenantId || !user.IsActive)
            return Result.Failure<HandoffSessionResponse>(invalid);

        // Sesión única: si el usuario ya tiene una sesión activa en la oficina, se exige takeover en
        // vez de materializar; si no, se emite. El flag de enrolamiento MFA viaja en el vale.
        var outcome = await SessionEstablishment.IssueOrRequireTakeoverAsync(
            user,
            tenant,
            ["pwd", "handoff"],
            command.DeviceName,
            mustEnrollMfa: payload.MustEnrollMfa,
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
                    AuthAuditAction.LoginSucceeded,
                    true,
                    request.IpAddress,
                    request.UserAgent,
                    correlation.CorrelationId,
                    detailsJson: """{"method":"handoff","takeoverRequired":true}"""
                ),
                ct
            );
            await unitOfWork.SaveChangesAsync(ct);
            return Result.Success(
                HandoffSessionResponse.ForTakeover(
                    outcome.TakeoverTicket!.Value.ToString(),
                    (int)LockoutPolicy.TakeoverTicketValidity.TotalSeconds,
                    payload.MustEnrollMfa
                )
            );
        }

        var issued = outcome.Tokens!;

        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                user.Id,
                AuthAuditAction.LoginSucceeded,
                true,
                request.IpAddress,
                request.UserAgent,
                correlation.CorrelationId,
                detailsJson: """{"method":"handoff"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            HandoffSessionResponse.ForTokens(
                issued.AccessToken,
                issued.RefreshToken,
                issued.ExpiresInSeconds,
                payload.MustEnrollMfa
            )
        );
    }
}
