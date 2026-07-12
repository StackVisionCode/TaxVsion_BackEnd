using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.Reactivate;

public static class ReactivateCustomerHandler
{
    public static async Task<Result> Handle(
        ReactivateCustomerCommand cmd,
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

        var result = customer.Reactivate(cmd.ModifiedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerReactivatedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                ReactivatedByUserId = cmd.ModifiedByUserId,
                ReactivatedAtUtc = DateTime.UtcNow,
            }
        );

        return Result.Success();
    }
}
