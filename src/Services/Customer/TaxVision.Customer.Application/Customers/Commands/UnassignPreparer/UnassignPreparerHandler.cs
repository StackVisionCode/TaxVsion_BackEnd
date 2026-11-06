using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.UnassignPreparer;

public static class UnassignPreparerHandler
{
    public static async Task<Result> Handle(
        UnassignPreparerCommand cmd,
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

        var result = customer.UnassignPreparer(cmd.UnassignedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerPreparerUnassignedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                UnassignedByUserId = cmd.UnassignedByUserId,
            }
        );

        return Result.Success();
    }
}
