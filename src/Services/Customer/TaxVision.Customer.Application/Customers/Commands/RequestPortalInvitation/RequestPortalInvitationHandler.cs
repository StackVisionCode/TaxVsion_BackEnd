using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;

public static class RequestPortalInvitationHandler
{
    public static async Task<Result<RequestPortalInvitationResponse>> Handle(
        RequestPortalInvitationCommand cmd,
        ICustomerRepository repository,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure<RequestPortalInvitationResponse>(
                new Error("Customer.NotFound", "Customer not found.")
            );

        if (customer.TenantId != cmd.TenantId)
            return Result.Failure<RequestPortalInvitationResponse>(
                new Error("Customer.NotFound", "Customer not found.") // mismo error — no revelar que existe en otro tenant
            );

        if (customer.Status == CustomerStatus.Archived)
            return Result.Failure<RequestPortalInvitationResponse>(
                new Error("Customer.Archived", "Cannot request portal access for an archived customer.")
            );

        await bus.PublishAsync(
            new CustomerPortalInvitationRequestedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                Email = customer.PrimaryEmail.Value,
                DisplayName = customer.DisplayName,
                RequestedByUserId = cmd.RequestedByUserId,
            }
        );

        return Result.Success(
            new RequestPortalInvitationResponse(customer.Id, customer.PrimaryEmail.Value, "Requested")
        );
    }
}
