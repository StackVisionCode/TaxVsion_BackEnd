using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Commands.Activate;

// CustomerActivated sale del evento de dominio (CustomerActivatedDomainEventHandler).
public static class ActivateCustomerHandler
{
    public static async Task<Result> Handle(
        ActivateCustomerCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.Activate(cmd.ModifiedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
