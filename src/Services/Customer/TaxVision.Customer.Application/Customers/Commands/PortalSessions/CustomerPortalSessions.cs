using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers.Commands.PortalMfa;

namespace TaxVision.Customer.Application.Customers.Commands.PortalSessions;

// Sesiones del portal de un cliente para la ficha del CRM. Misma regla que el resto de la ficha: otro tenant
// o un cliente no asignado (sin customers.view_all) se comporta como inexistente y Auth no se llama.

/// <summary>Historial de sesiones del portal del cliente; sin el permiso de ubicación, sin ubicación.</summary>
public sealed record GetCustomerPortalSessionsQuery(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    bool CanSeeLocation,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null,
    int? Page = null,
    int? PageSize = null
);

public static class GetCustomerPortalSessionsHandler
{
    public static async Task<Result<PortalSessionPage>> Handle(
        GetCustomerPortalSessionsQuery query,
        ICustomerRepository repository,
        ICustomerPortalSessionsClient sessions,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(query.TenantId, query.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, query.TenantId, query.ActorUserId, query.CanViewAll))
            return Result.Failure<PortalSessionPage>(PortalMfaAccess.NotFound);

        var result = await sessions.ListSessionsAsync(
            customer!.TenantId,
            customer.Id,
            new PortalSessionsQuery(query.FromUtc, query.ToUtc, query.Page, query.PageSize),
            ct
        );
        if (result.IsFailure || query.CanSeeLocation)
            return result;

        var page = result.Value;
        return Result.Success(
            page with
            {
                Items = page.Items.Select(session => session with { Location = null, LocationHidden = true }).ToList(),
            }
        );
    }
}

/// <summary>La oficina cierra una sesión del portal del cliente.</summary>
public sealed record RevokeCustomerPortalSessionCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid SessionId,
    Guid ActorUserId,
    bool CanViewAll
);

public static class RevokeCustomerPortalSessionHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerPortalSessionCommand command,
        ICustomerRepository repository,
        ICustomerPortalSessionsClient sessions,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure(PortalMfaAccess.NotFound);

        return await sessions.RevokeSessionAsync(
            customer!.TenantId,
            customer.Id,
            command.SessionId,
            command.ActorUserId,
            ct
        );
    }
}

/// <summary>La oficina cierra todas las sesiones del portal del cliente.</summary>
public sealed record RevokeCustomerPortalSessionsCommand(
    Guid TenantId,
    Guid CustomerId,
    Guid ActorUserId,
    bool CanViewAll,
    bool ForgetTrustedDevices
);

public static class RevokeCustomerPortalSessionsHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerPortalSessionsCommand command,
        ICustomerRepository repository,
        ICustomerPortalSessionsClient sessions,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(command.TenantId, command.CustomerId, ct);
        if (!PortalMfaAccess.CanUse(customer, command.TenantId, command.ActorUserId, command.CanViewAll))
            return Result.Failure(PortalMfaAccess.NotFound);

        return await sessions.RevokeAllSessionsAsync(
            customer!.TenantId,
            customer.Id,
            command.ActorUserId,
            command.ForgetTrustedDevices,
            ct
        );
    }
}
