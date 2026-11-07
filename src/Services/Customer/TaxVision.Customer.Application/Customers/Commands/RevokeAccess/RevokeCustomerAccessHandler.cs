using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;

namespace TaxVision.Customer.Application.Customers.Commands.RevokeAccess;

// Revoca el acceso de un miembro del staff a un cliente. Si era el primary, limpia la denormalización.
// El snapshot de asignaciones sale del evento de dominio.
public static class RevokeCustomerAccessHandler
{
    public static async Task<Result> Handle(
        RevokeCustomerAccessCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.TenantId, cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.RevokeAccess(cmd.UserId, cmd.RevokedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success();
    }
}
