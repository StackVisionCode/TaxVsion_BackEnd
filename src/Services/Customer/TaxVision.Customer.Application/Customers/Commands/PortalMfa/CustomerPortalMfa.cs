using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Customers.Commands.PortalMfa;

/// <summary>
/// Segundo factor del portal de un cliente, para la ficha del CRM. Misma regla que el resto de la ficha:
/// otro tenant o un cliente no asignado (sin <c>customers.view_all</c>) se comporta como inexistente.
/// </summary>
public sealed record GetCustomerPortalMfaQuery(Guid TenantId, Guid CustomerId, Guid ActorUserId, bool CanViewAll);

/// <summary>
/// La oficina quita el segundo factor del portal del cliente tras verificar su identidad. Step-up, IP real y motivo
/// (código de Auth como texto, nota ≤ 256) viajan a Auth, que decide quién puede (solo el Owner hoy).
/// </summary>
public sealed record ResetCustomerPortalMfaCommand(
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

internal static class PortalMfaAccess
{
    public static readonly Error NotFound = new("Customer.NotFound", "Customer not found.");

    public static bool CanUse(CustomerEntity? customer, Guid tenantId, Guid actorUserId, bool canViewAll) =>
        customer is not null
        && customer.TenantId == tenantId
        && CustomerAccessPolicy.CanAccess(customer, actorUserId, canViewAll);
}

public static class GetCustomerPortalMfaHandler
{
    public static async Task<Result<PortalMfaStatus>> Handle(
        GetCustomerPortalMfaQuery query,
        ICustomerRepository repository,
        ICustomerPortalAccessClient portalAccess,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(query.TenantId, query.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, query.TenantId, query.ActorUserId, query.CanViewAll))
            return Result.Failure<PortalMfaStatus>(PortalMfaAccess.NotFound);

        return await portalAccess.GetMfaStatusAsync(customer!.TenantId, customer.Id, ct);
    }
}

public static class ResetCustomerPortalMfaHandler
{
    public static async Task<Result<PortalMfaResetOutcome>> Handle(
        ResetCustomerPortalMfaCommand command,
        ICustomerRepository repository,
        ICustomerPortalAccessClient portalAccess,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure<PortalMfaResetOutcome>(PortalMfaAccess.NotFound);

        return await portalAccess.ResetMfaAsync(
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
