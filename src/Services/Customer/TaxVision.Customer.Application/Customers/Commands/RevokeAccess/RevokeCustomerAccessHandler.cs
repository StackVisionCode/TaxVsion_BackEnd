using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.RevokeAccess;

// Revoca el acceso de un miembro del staff a un cliente. Si era el primary, limpia la denormalización.
public static class RevokeCustomerAccessHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerAccessCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.RevokeAccess(cmd.UserId, cmd.RevokedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId));

        return Result.Success();
    }
}
