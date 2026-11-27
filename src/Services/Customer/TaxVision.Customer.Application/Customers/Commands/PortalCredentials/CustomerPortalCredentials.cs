using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalMfa;

namespace TaxVision.Customer.Application.Customers.Commands.PortalCredentials;

// Link de reset del portal de un cliente desde la ficha (03 §14.2). Misma visibilidad que el resto de la ficha:
// otro tenant o un cliente no asignado (sin customers.view_all) se comporta como inexistente. Auth decide quién de
// la oficina puede (solo el Owner hoy) y vuelve a validar la reautenticación que viaja en el sobre.

public sealed record GetCustomerPortalCredentialsQuery(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll
);

public sealed record SendCustomerPortalResetLinkCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent
);

public sealed record CancelCustomerPortalResetLinkCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent
);

/// <summary>Cambio obligatorio del portal del cliente (03 §14.2). Mismo ABAC; Auth valida motivo y política.</summary>
public sealed record RequireCustomerPortalPasswordChangeCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent,
    string ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed
);

/// <summary>Asegura la cuenta del portal y envía el link seguro; de nuevo, lo reenvía (AJ-15).</summary>
public sealed record SecureCustomerPortalAccountCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent,
    string ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed
);

public static class GetCustomerPortalCredentialsHandler
{
    public static async Task<Result<PortalCredentialStatusResponse>> Handle(
        GetCustomerPortalCredentialsQuery query,
        ICustomerRepository repository,
        ICustomerPortalCredentialsClient credentials,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(query.TenantId, query.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, query.TenantId, query.ActorUserId, query.CanViewAll))
            return Result.Failure<PortalCredentialStatusResponse>(PortalMfaAccess.NotFound);

        return await credentials.GetStatusAsync(customer!.TenantId, customer.Id, query.ActorUserId, ct);
    }
}

public static class SendCustomerPortalResetLinkHandler
{
    public static async Task<Result<ResetLinkIssuedResponse>> Handle(
        SendCustomerPortalResetLinkCommand command,
        ICustomerRepository repository,
        ICustomerPortalCredentialsClient credentials,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure<ResetLinkIssuedResponse>(PortalMfaAccess.NotFound);

        return await credentials.SendResetLinkAsync(
            customer!.TenantId,
            customer.Id,
            new PortalCredentialRequest(
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                command.ActorIpAddress,
                command.ActorUserAgent
            ),
            ct
        );
    }
}

public static class CancelCustomerPortalResetLinkHandler
{
    public static async Task<Result> Handle(
        CancelCustomerPortalResetLinkCommand command,
        ICustomerRepository repository,
        ICustomerPortalCredentialsClient credentials,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure(PortalMfaAccess.NotFound);

        return await credentials.CancelResetLinkAsync(
            customer!.TenantId,
            customer.Id,
            new PortalCredentialRequest(
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                command.ActorIpAddress,
                command.ActorUserAgent
            ),
            ct
        );
    }
}

public static class RequireCustomerPortalPasswordChangeHandler
{
    public static async Task<Result<CredentialStateChangedResponse>> Handle(
        RequireCustomerPortalPasswordChangeCommand command,
        ICustomerRepository repository,
        ICustomerPortalCredentialsClient credentials,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure<CredentialStateChangedResponse>(PortalMfaAccess.NotFound);

        return await credentials.RequireChangeAsync(
            customer!.TenantId,
            customer.Id,
            new PortalCredentialRequest(
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                command.ActorIpAddress,
                command.ActorUserAgent,
                command.ReasonCode,
                command.ReasonNote,
                command.IdentityConfirmed
            ),
            ct
        );
    }
}

public static class SecureCustomerPortalAccountHandler
{
    public static async Task<Result<AccountSecuredResponse>> Handle(
        SecureCustomerPortalAccountCommand command,
        ICustomerRepository repository,
        ICustomerPortalCredentialsClient credentials,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure<AccountSecuredResponse>(PortalMfaAccess.NotFound);

        return await credentials.SecureAsync(
            customer!.TenantId,
            customer.Id,
            new PortalCredentialRequest(
                command.ActorUserId,
                command.ReauthenticatedAtUtc,
                command.ActorIpAddress,
                command.ActorUserAgent,
                command.ReasonCode,
                command.ReasonNote,
                command.IdentityConfirmed
            ),
            ct
        );
    }
}
