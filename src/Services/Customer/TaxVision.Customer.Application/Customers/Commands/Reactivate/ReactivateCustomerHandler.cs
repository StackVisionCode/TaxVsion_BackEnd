using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Commands.Reactivate;

// CustomerReactivated sale del evento de dominio (CustomerReactivatedDomainEventHandler).
public static class ReactivateCustomerHandler
{
    public static async Task<Result> Handle(
        ReactivateCustomerCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.TenantId, cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.Reactivate(cmd.ModifiedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
