using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Customer.Application.Abstractions;
using Wolverine;

namespace TaxVision.Customer.Application.Customers.Commands.BulkAssign;

// Asigna UN usuario a MUCHOS clientes de un tirón (el admin reparte su cartera). Carga por lote los
// clientes asignables (1 query con Include) y los muta con el agregado. "Automático": si el cliente NO
// tenía responsable, el asignado queda de PRIMARY; si ya tenía, entra como acceso adicional. Tope por
// request para no saturar; 1 SaveChanges (EF batchea). Los repartos masivos de una vez van por el backfill.
public static class BulkAssignCustomersHandler
{
    public const int MaxItemsPerCall = 500;

    public static async Task<Result<BulkAssignResponse>> Handle(
        BulkAssignCustomersCommand cmd,
        ICustomerRepository repository,
        ITenantEmployeeDirectoryRepository employeeDirectory,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (cmd.UserId == Guid.Empty)
            return Result.Failure<BulkAssignResponse>(new Error("Customer.InvalidAssignee", "UserId is required."));
        if (cmd.CustomerIds.Count == 0)
            return Result.Failure<BulkAssignResponse>(new Error("Bulk.Empty", "At least one customerId is required."));
        if (cmd.CustomerIds.Count > MaxItemsPerCall)
            return Result.Failure<BulkAssignResponse>(
                new Error("Bulk.TooMany", $"Bulk assign supports max {MaxItemsPerCall} items per call.")
            );

        // El asignado debe ser staff activo del mismo tenant (misma invariante que AssignPreparer) — 1 lookup.
        var employee = await employeeDirectory.GetByUserIdAsync(cmd.UserId, ct);
        if (employee is null || employee.TenantId != cmd.TenantId || !employee.IsEligiblePreparer)
            return Result.Failure<BulkAssignResponse>(
                new Error("Customer.AssigneeNotEligible", "UserId must be an active staff member of the same tenant.")
            );

        var uniqueIds = cmd.CustomerIds.Distinct().ToList();
        var customers = await repository.ListForAssignmentAsync(cmd.TenantId, uniqueIds, ct);

        var granted = new List<Guid>(); // acceso adicional (el cliente ya tenía responsable)
        var madePrimary = new List<Guid>(); // quedó de responsable (cliente sin primary previo)
        var already = 0;

        foreach (var customer in customers)
        {
            // Ya estaba asignado (primary o acceso) → idempotente, no se toca.
            if (customer.Assignments.Any(a => a.UserId == cmd.UserId))
            {
                already++;
                continue;
            }

            // "Automático": sin responsable → queda de responsable; con responsable → solo acceso.
            if (customer.AssignedPreparerUserId is null)
            {
                if (customer.AssignPreparer(cmd.UserId, cmd.AssignedByUserId).IsSuccess)
                    madePrimary.Add(customer.Id);
            }
            else if (customer.GrantAccess(cmd.UserId, cmd.AssignedByUserId).IsSuccess)
            {
                granted.Add(customer.Id);
            }
        }

        if (madePrimary.Count > 0 || granted.Count > 0)
            await unitOfWork.SaveChangesAsync(ct);

        // El responsable se anuncia por cliente (Communication rerutea el preparador).
        foreach (var id in madePrimary)
            await bus.PublishAsync(
                new CustomerPreparerAssignedIntegrationEvent
                {
                    TenantId = cmd.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    CustomerId = id,
                    PreparerUserId = cmd.UserId,
                    AssignedByUserId = cmd.AssignedByUserId,
                }
            );

        // Snapshot por cliente afectado — fuente canónica para las proyecciones de visibilidad downstream.
        var affected = new HashSet<Guid>(madePrimary);
        affected.UnionWith(granted);
        foreach (var customer in customers.Where(c => affected.Contains(c.Id)))
            await bus.PublishAsync(CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId));

        var notFound = uniqueIds.Count - customers.Count;
        return Result.Success(
            new BulkAssignResponse(uniqueIds.Count, madePrimary.Count + granted.Count, already, notFound)
        );
    }
}
