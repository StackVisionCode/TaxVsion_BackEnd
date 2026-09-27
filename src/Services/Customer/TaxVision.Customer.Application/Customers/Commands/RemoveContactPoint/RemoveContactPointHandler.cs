using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Commands.RemoveContactPoint;

public static class RemoveContactPointHandler
{
    public static async Task<Result> Handle(
        RemoveContactPointCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (
            customer is null
            || customer.TenantId != cmd.TenantId
            // Visibilidad por asignación: un cliente que no le toca se comporta como inexistente,
            // igual que en la lectura. Un 403 confirmaría que ese id existe.
            || !CustomerAccessPolicy.CanAccess(customer, cmd.ModifiedByUserId, cmd.CallerCanViewAllCustomers)
        )
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.RemoveContactPoint(cmd.ContactPointId, cmd.ModifiedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success();
    }
}
