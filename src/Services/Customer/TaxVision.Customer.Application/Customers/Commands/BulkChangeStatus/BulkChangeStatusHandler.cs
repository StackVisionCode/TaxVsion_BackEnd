using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

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
        var validation = ValidateInput(cmd);
        if (validation.IsFailure)
            return Result.Failure<BulkStatusActionResponse>(validation.Error);

        var uniqueIds = cmd.CustomerIds.Distinct().ToList();

        // 1 sola query BD para los N customers (evita N+1)
        var customers = await repository.GetByIdsAsync(cmd.TenantId, uniqueIds, ct);
        var customerById = customers.ToDictionary(c => c.Id);

        // Estrategias resueltas UNA vez fuera del loop
        var applyAction = ResolveActionRunner(cmd.Action);
        var makeEvent = ResolveEventFactory(cmd.Action, cmd.TenantId, cmd.ModifiedByUserId, correlation.CorrelationId);

        var succeededIds = new List<Guid>(uniqueIds.Count);
        var failures = new List<BulkFailedItem>();

        foreach (var id in uniqueIds)
        {
            if (!customerById.TryGetValue(id, out var customer))
            {
                failures.Add(new BulkFailedItem(id, "Customer.NotFound", "Customer not found."));
                continue;
            }

            var result = applyAction(customer, cmd.ModifiedByUserId);
            if (result.IsFailure)
            {
                failures.Add(new BulkFailedItem(id, result.Error.Code, result.Error.Message));
                continue;
            }

            succeededIds.Add(id);
        }

        await unitOfWork.SaveChangesAsync(ct);

        foreach (var id in succeededIds)
            await bus.PublishAsync(makeEvent(id));

        return Result.Success(
            new BulkStatusActionResponse(uniqueIds.Count, succeededIds.Count, failures.Count, failures)
        );
    }

    private static Result ValidateInput(BulkChangeStatusCommand cmd)
    {
        if (cmd.CustomerIds.Count == 0)
            return Result.Failure(new Error("Bulk.Empty", "At least one customerId is required."));
        if (cmd.CustomerIds.Count > MaxItemsPerCall)
            return Result.Failure(
                new Error("Bulk.TooMany", $"Bulk operations support max {MaxItemsPerCall} items per call.")
            );
        return Result.Success();
    }

    /// <summary>
    /// Strategy: mapea el enum BulkStatusAction al metodo correspondiente del aggregate Customer.
    /// Delegado devuelto se invoca N veces sin re-evaluar el switch.
    /// </summary>
    private static Func<CustomerEntity, Guid, Result> ResolveActionRunner(BulkStatusAction action) =>
        action switch
        {
            BulkStatusAction.Archive => (c, u) => c.Archive(u),
            BulkStatusAction.Reactivate => (c, u) => c.Reactivate(u),
            BulkStatusAction.Activate => (c, u) => c.Activate(u),
            BulkStatusAction.Deactivate => (c, u) => c.Deactivate(u),
            _ => (_, _) => Result.Failure(new Error("Bulk.UnknownAction", $"Unknown action {action}.")),
        };

    /// <summary>
    /// Strategy: mapea el enum BulkStatusAction al factory de su evento de integracion.
    /// Captura las variables del contexto (tenant, user, correlation, timestamp) una sola vez.
    /// </summary>
    private static Func<Guid, IntegrationEvent> ResolveEventFactory(
        BulkStatusAction action,
        Guid tenantId,
        Guid userId,
        string correlationId
    )
    {
        var nowUtc = DateTime.UtcNow;

        return action switch
        {
            BulkStatusAction.Archive => id => new CustomerArchivedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                CustomerId = id,
                ArchivedByUserId = userId,
            },
            BulkStatusAction.Reactivate => id => new CustomerReactivatedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                CustomerId = id,
                ReactivatedByUserId = userId,
                ReactivatedAtUtc = nowUtc,
            },
            BulkStatusAction.Activate => id => new CustomerActivatedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                CustomerId = id,
                ActivatedByUserId = userId,
                ActivatedAtUtc = nowUtc,
            },
            BulkStatusAction.Deactivate => id => new CustomerDeactivatedIntegrationEvent
            {
                TenantId = tenantId,
                CorrelationId = correlationId,
                CustomerId = id,
                DeactivatedByUserId = userId,
                DeactivatedAtUtc = nowUtc,
            },
            _ => throw new InvalidOperationException($"Unknown action {action}"),
        };
    }
}
