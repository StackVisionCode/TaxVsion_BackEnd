using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Credentials.Administration;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Application.Customers.Commands;

// ---------------------------------------------------------------------------
// Segundo factor del portal de UN cliente, visto y reseteado por la oficina (vía Customer, M2M).
// Customer ya comprobó el permiso y que el cliente es visible para quien lo pide.
// ---------------------------------------------------------------------------

/// <summary><see cref="HasPortalAccount"/> false = el cliente aún no tiene acceso al portal.</summary>
public sealed record CustomerPortalMfaStatus(bool HasPortalAccount, bool MfaEnabled, bool RequiredByPolicy);

public sealed record GetCustomerPortalMfaQuery(Guid TenantId, Guid CustomerId);

public static class GetCustomerPortalMfaHandler
{
    public static async Task<Result<CustomerPortalMfaStatus>> Handle(
        GetCustomerPortalMfaQuery query,
        IUserRepository users,
        IMfaRepository mfa,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(query.TenantId, query.CustomerId, ct);
        if (user is null)
            return Result.Success(new CustomerPortalMfaStatus(false, false, false));

        return Result.Success(
            new CustomerPortalMfaStatus(
                true,
                user.MfaEnabled,
                await MfaRequirement.IsRequiredByPolicyAsync(user, mfa, ct)
            )
        );
    }
}

public static class CustomerPortalMfaResetOutcome
{
    /// <summary>Se quitó el segundo factor: el cliente entra con su contraseña y puede volver a activarlo.</summary>
    public const string Reset = "Reset";

    /// <summary>No tenía segundo factor: no había nada que quitar.</summary>
    public const string NotEnabled = "NotEnabled";
}

public sealed record ResetCustomerPortalMfaResult(string Outcome);

public sealed record ResetCustomerPortalMfaCommand(
    Guid TenantId,
    Guid CustomerId,
    PortalCredentialCommandEnvelope Envelope,
    string CorrelationId
);

/// <summary>
/// El cliente perdió el teléfono (o los códigos): la oficina, tras verificar su identidad, le quita el
/// segundo factor. Si la oficina lo exige, el próximo login le pide enrolarlo de nuevo. Endurecido (CS-10):
/// misma política y ventana de reautenticación que el resto de la administración de credenciales, motivo
/// obligatorio (D-16), sesiones y dispositivos revocados, auditoría con la IP real del actor y alerta
/// <c>mfa_reset_by_office</c>.
/// </summary>
public static class ResetCustomerPortalMfaHandler
{
    public static readonly Error NoPortalAccount = new(
        "Auth.NoPortalAccount",
        "This client doesn't have portal access yet."
    );

    public static async Task<Result<ResetCustomerPortalMfaResult>> Handle(
        ResetCustomerPortalMfaCommand command,
        IUserRepository users,
        IUserLoginLock userLock,
        SessionAccessCutoff cutoff,
        IMfaRepository mfa,
        IOptions<CredentialAdministrationOptions> options,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (user is null)
            return Result.Failure<ResetCustomerPortalMfaResult>(NoPortalAccount);

        var trail = PortalCredentialAudit.For(command.TenantId, command.Envelope, command.CorrelationId, audit);
        if (
            await PortalCredentialAudit.RejectStaleAsync(
                trail,
                user,
                OfficeCredentialAdministration.ResetMfaModality,
                unitOfWork,
                ct
            ) is
            { } stale
        )
            return Result.Failure<ResetCustomerPortalMfaResult>(stale);
        if (PortalCredentialAudit.ReasonOf(command.Envelope) is not { } request)
            return Result.Failure<ResetCustomerPortalMfaResult>(CredentialAdministrationErrors.ReasonRequired);

        var reset = await OfficeCredentialAdministration.ResetMfaAsync(
            user.Id,
            user,
            request,
            options.Value,
            users,
            userLock,
            cutoff,
            mfa,
            trail,
            unitOfWork,
            bus,
            ct
        );
        return reset.IsSuccess
            ? Result.Success(new ResetCustomerPortalMfaResult(reset.Value.Outcome))
            : Result.Failure<ResetCustomerPortalMfaResult>(reset.Error);
    }
}
