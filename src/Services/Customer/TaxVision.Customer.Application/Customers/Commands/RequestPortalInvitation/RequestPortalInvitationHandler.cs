using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;

/// <summary>
/// Pide a Auth el acceso al portal del cliente con su email principal y devuelve el desenlace real
/// (invitado, reenviado o ya activo) o el motivo del rechazo, para que el CRM lo muestre.
/// </summary>
public static class RequestPortalInvitationHandler
{
    public static async Task<Result<RequestPortalInvitationResponse>> Handle(
        RequestPortalInvitationCommand cmd,
        ICustomerRepository repository,
        ICustomerPortalAccessClient portalAccess,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure<RequestPortalInvitationResponse>(
                new Error("Customer.NotFound", "Customer not found.") // mismo error — no revelar que existe en otro tenant
            );

        if (customer.Status == CustomerStatus.Archived)
            return Result.Failure<RequestPortalInvitationResponse>(
                new Error("Customer.Archived", "Cannot request portal access for an archived customer.")
            );

        var outcome = await portalAccess.IssueInvitationAsync(
            customer.TenantId,
            customer.Id,
            customer.PrimaryEmail.Value,
            cmd.RequestedByUserId,
            ct
        );
        if (outcome.IsFailure)
            return Result.Failure<RequestPortalInvitationResponse>(outcome.Error);

        return Result.Success(
            new RequestPortalInvitationResponse(
                customer.Id,
                outcome.Value.Email,
                outcome.Value.Outcome,
                outcome.Value.ExpiresAtUtc
            )
        );
    }
}
