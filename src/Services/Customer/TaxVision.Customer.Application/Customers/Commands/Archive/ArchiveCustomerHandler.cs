using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.Archive;

public static class ArchiveCustomerHandler
{
    public static async Task<Result> Handle(
        ArchiveCustomerCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var customer = await repository.GetByIdAsync(cmd.CustomerId, ct);
        if (customer is null)
            return Result.Failure(new Error("Customer.NotFound", "Customer not found."));

        var result = customer.Archive(cmd.ArchivedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new CustomerArchivedIntegrationEvent
            {
                TenantId = customer.TenantId,
                CorrelationId = correlation.CorrelationId,
                CustomerId = customer.Id,
                ArchivedByUserId = cmd.ArchivedByUserId,
            }
        );

        return Result.Success();
    }
}
