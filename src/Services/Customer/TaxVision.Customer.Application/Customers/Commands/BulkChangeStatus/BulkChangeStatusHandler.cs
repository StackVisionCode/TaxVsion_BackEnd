using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.BulkChangeStatus;

public static class BulkChangeStatusHandler
{
    public const int MaxItemsPerCall = 100;

    public static async Task<Result<BulkStatusActionResponse>> Handle(
        BulkChangeStatusCommand cmd,
        ICustomerRepository repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (cmd.CustomerIds.Count == 0)
            return Result.Failure<BulkStatusActionResponse>(
                new Error("Bulk.Empty", "At least one customerId is required.")
            );
        if (cmd.CustomerIds.Count > MaxItemsPerCall)
            return Result.Failure<BulkStatusActionResponse>(
                new Error("Bulk.TooMany", $"Bulk operations support max {MaxItemsPerCall} items per call.")
            );

        var failures = new List<BulkFailedItem>();
        var succeededIds = new List<Guid>();
        var nowUtc = DateTime.UtcNow;

        foreach (var customerId in cmd.CustomerIds.Distinct())
        {
            var customer = await repository.GetByIdAsync(customerId, ct);
            if (customer is null || customer.TenantId != cmd.TenantId)
            {
                failures.Add(new BulkFailedItem(customerId, "Customer.NotFound", "Customer not found."));
                continue;
            }

            var result = cmd.Action switch
            {
                BulkStatusAction.Archive => customer.Archive(cmd.ModifiedByUserId),
                BulkStatusAction.Reactivate => customer.Reactivate(cmd.ModifiedByUserId),
                BulkStatusAction.Activate => customer.Activate(cmd.ModifiedByUserId),
                BulkStatusAction.Deactivate => customer.Deactivate(cmd.ModifiedByUserId),
                _ => Result.Failure(new Error("Bulk.UnknownAction", $"Unknown action {cmd.Action}.")),
            };

            if (result.IsFailure)
            {
                failures.Add(new BulkFailedItem(customerId, result.Error.Code, result.Error.Message));
                continue;
            }

            succeededIds.Add(customerId);
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var id in succeededIds)
        {
            IntegrationEvent? evt = cmd.Action switch
            {
                BulkStatusAction.Archive => new CustomerArchivedIntegrationEvent
                {
                    TenantId = cmd.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = id,
                    ArchivedByUserId = cmd.ModifiedByUserId,
                },
                BulkStatusAction.Reactivate => new CustomerReactivatedIntegrationEvent
                {
                    TenantId = cmd.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = id,
                    ReactivatedByUserId = cmd.ModifiedByUserId,
                    ReactivatedAtUtc = nowUtc,
                },
                BulkStatusAction.Activate => new CustomerActivatedIntegrationEvent
                {
                    TenantId = cmd.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = id,
                    ActivatedByUserId = cmd.ModifiedByUserId,
                    ActivatedAtUtc = nowUtc,
                },
                BulkStatusAction.Deactivate => new CustomerDeactivatedIntegrationEvent
                {
                    TenantId = cmd.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = id,
                    DeactivatedByUserId = cmd.ModifiedByUserId,
                    DeactivatedAtUtc = nowUtc,
                },
                _ => null,
            };
            if (evt is not null)
                await bus.PublishAsync(evt);
        }

        return Result.Success(
            new BulkStatusActionResponse(cmd.CustomerIds.Count, succeededIds.Count, failures.Count, failures)
        );
    }
}
