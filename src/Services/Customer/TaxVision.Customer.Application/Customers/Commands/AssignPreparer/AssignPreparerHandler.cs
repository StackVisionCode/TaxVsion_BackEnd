using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.AssignPreparer;

public static class AssignPreparerHandler
{
    public static async Task<Result> Handle(
        AssignPreparerCommand cmd,
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

        // El preparador debe ser un empleado activo del mismo tenant — invariante que le
        // corresponde a Customer proteger (gap encontrado en la auditoria del track de
        // chat tipado: antes solo se chequeaba que el GUID no fuera vacio).
        var employee = await employeeDirectory.GetByUserIdAsync(cmd.PreparerUserId, ct);
        if (employee is null || employee.TenantId != cmd.TenantId || !employee.IsEligiblePreparer)
        {
            return Result.Failure(
                new Error(
                    "Customer.PreparerNotEligible",
                    "PreparerUserId must be an active TenantEmployee or TenantAdmin of the same tenant."
                )
            );
        }

        var result = customer.AssignPreparer(cmd.PreparerUserId, cmd.AssignedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerPreparerAssignedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                PreparerUserId = cmd.PreparerUserId,
                AssignedByUserId = cmd.AssignedByUserId,
            }
        );

        return Result.Success();
    }
}
