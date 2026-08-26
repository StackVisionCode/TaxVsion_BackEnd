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
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    bool MfaSetupRequired
);

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

        var issued = await SessionEstablishment.IssueAsync(
            user,
            tenant,
            ["pwd", "handoff"],
            command.DeviceName,
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
                detailsJson: """{"method":"handoff"}"""
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(
            new HandoffSessionResponse(
                issued.AccessToken,
                issued.RefreshToken,
                issued.ExpiresInSeconds,
                payload.MustEnrollMfa
            )
        );
    }
}
