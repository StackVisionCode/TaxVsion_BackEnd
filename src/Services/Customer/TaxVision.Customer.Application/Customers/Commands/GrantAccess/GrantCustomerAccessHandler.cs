using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.GrantAccess;

// Da acceso a un cliente a un miembro del staff (asignación adicional, no primary). Solo el admin reparte.
public static class GrantCustomerAccessHandler
{
    public static async Task<Result> Handle(
        GrantCustomerAccessCommand cmd,
        ICustomerRepository repository,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null || customer.TenantId != cmd.TenantId)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        // El asignado debe ser staff activo del mismo tenant (misma invariante que AssignPreparer).
        var employee = await employeeDirectory.GetByUserIdAsync(cmd.UserId, ct);
        if (employee is null || employee.TenantId != cmd.TenantId || !employee.IsEligiblePreparer)
            return Result.Failure(
                new Error("Customer.AssigneeNotEligible", "UserId must be an active staff member of the same tenant.")
            );

        var result = customer.GrantAccess(cmd.UserId, cmd.GrantedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId));

        return Result.Success();
    }
}
