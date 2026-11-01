using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Application.Common;
using TaxVision.Auth.Application.Mfa;
using TaxVision.Auth.Domain.Audit;
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
    Guid RequestedByUserId,
    string CorrelationId
);

/// <summary>
/// El cliente perdió el teléfono (o los códigos): la oficina, tras verificar su identidad, le quita el
/// segundo factor. Si la oficina lo exige, el próximo login le pide enrolarlo de nuevo. Queda auditado
/// con quién lo hizo y el cliente recibe la alerta de seguridad de "segundo factor desactivado".
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
        IMfaRepository mfa,
        IAuthAuditWriter audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var user = await users.GetPortalUserByCustomerAsync(command.TenantId, command.CustomerId, ct);
        if (user is null)
            return Result.Failure<ResetCustomerPortalMfaResult>(NoPortalAccount);

        var hasFactors = user.MfaEnabled || (await mfa.GetMethodsAsync(user.Id, ct)).Count > 0;
        if (!hasFactors)
            return Result.Success(new ResetCustomerPortalMfaResult(CustomerPortalMfaResetOutcome.NotEnabled));

        await MfaFactors.ClearAsync(user, mfa, ct);

        var details = JsonSerializer.Serialize(
            new { resetBy = "office", requestedByUserId = command.RequestedByUserId }
        );
        await bus.PublishAsync(
            new SecurityAlertIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                AlertType = SecurityAlertType.MfaDisabled,
                DetailsJson = details,
                CorrelationId = command.CorrelationId,
            }
        );

        // La IP y el agente serían los del servicio que llama; quién lo pidió va como actor del registro.
        await audit.AddAsync(
            AuthAuditLog.Record(
                user.TenantId,
                command.RequestedByUserId,
                AuthAuditAction.MfaResetByOffice,
                true,
                ipAddress: null,
                userAgent: null,
                command.CorrelationId,
                targetType: "User",
                targetId: user.Id
            ),
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new ResetCustomerPortalMfaResult(CustomerPortalMfaResetOutcome.Reset));
    }
}
