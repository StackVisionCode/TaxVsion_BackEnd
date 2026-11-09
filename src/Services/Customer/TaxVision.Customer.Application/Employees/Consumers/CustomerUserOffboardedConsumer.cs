using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Employees;
using Wolverine;
using CustomerEntity = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Application.Employees.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: (1) marca el directorio como retirado — deja de ser preparador
/// elegible; (2) traspasa sus clientes PRIMARY activos al sucesor (o los desasigna si no hay) para que la
/// oficina siga trabajándolos; (3) le revoca los accesos ADICIONALES (no-primary); y (4) lo suelta de los
/// clientes ARCHIVADOS, sin asignar a nadie, para que al reactivarlos no vuelvan con un preparador retirado.
/// Idempotente; por lotes (keyset). Publica por cliente el preparador para Communication; el snapshot de
/// asignación para las proyecciones de visibilidad sale del evento de dominio.
/// </summary>
public static class CustomerUserOffboardedConsumer
{
    private const int BatchSize = 200;

    public static async Task Handle(
        UserOffboardedIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        ICustomerRepository customers,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantEmployeeDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        using var _ = correlation.Push(
            string.IsNullOrWhiteSpace(msg.CorrelationId) ? msg.EventId.ToString("N") : msg.CorrelationId
        );

        // Directorio: retirado (terminal) — nunca vuelve a ser preparador elegible.
        await directory.MarkOffboardedAsync(msg.UserId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        var successor = await ResolveEligibleSuccessorAsync(msg, directory, ct);
        var correlationId = correlation.CorrelationId;

        var primary = await SweepAsync(
            afterId => customers.ListByAssignedPreparerAsync(msg.TenantId, msg.UserId, BatchSize, afterId, ct),
            customer => HandOverOrUnassignAsync(customer, successor, msg, bus, correlationId),
            unitOfWork,
            ct
        );
        var extra = await SweepAsync(
            afterId => customers.ListNonPrimaryAssignedAsync(msg.TenantId, msg.UserId, BatchSize, afterId, ct),
            customer => RevokeExtraAccessAsync(customer, msg, bus, correlationId),
            unitOfWork,
            ct
        );
        var archived = await SweepAsync(
            afterId => customers.ListArchivedAssignedAsync(msg.TenantId, msg.UserId, BatchSize, afterId, ct),
            customer => ReleaseFromArchivedAsync(customer, msg, bus, correlationId),
            unitOfWork,
            ct
        );

        if (primary > 0 || extra > 0 || archived > 0)
            logger.LogInformation(
                "Offboarded {UserId} in tenant {TenantId}: {Primary} primary client(s) handed over/unassigned, "
                    + "{Extra} extra access(es) revoked, {Archived} archived client(s) released.",
                msg.UserId,
                msg.TenantId,
                primary,
                extra,
                archived
            );
    }

    // La invariante "el preparador es un empleado activo" la valida el handler, no el aggregate: se resuelve
    // una sola vez. Si no es elegible (o es el mismo que se retira), se desasigna en vez de asignar a un
    // preparador inválido — la oficina retoma la cola sin dueño.
    private static async Task<Guid?> ResolveEligibleSuccessorAsync(
        UserOffboardedIntegrationEvent msg,
        ITenantEmployeeDirectoryRepository directory,
        CancellationToken ct
    )
    {
        if (msg.SuccessorUserId is not { } candidate || candidate == msg.UserId)
            return null;

        var entry = await directory.GetByUserIdAsync(candidate, ct);
        return entry is not null && entry.TenantId == msg.TenantId && entry.IsEligiblePreparer ? candidate : null;
    }

    // Keyset por Id que avanza siempre (sin bucle infinito aunque un paso falle); guarda por lote.
    private static async Task<int> SweepAsync(
        Func<Guid, Task<IReadOnlyList<CustomerEntity>>> loadBatch,
        Func<CustomerEntity, Task<bool>> apply,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var afterId = Guid.Empty;
        var changed = 0;
        while (true)
        {
            var batch = await loadBatch(afterId);
            if (batch.Count == 0)
                break;

            foreach (var customer in batch)
            {
                if (await apply(customer))
                    changed++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            afterId = batch[^1].Id;
            if (batch.Count < BatchSize)
                break;
        }

        return changed;
    }

    // Handover: el sucesor queda primary y el saliente pierde la fila (no la conserva degradada).
    private static async Task<bool> HandOverOrUnassignAsync(
        CustomerEntity customer,
        Guid? successor,
        UserOffboardedIntegrationEvent msg,
        IMessageBus bus,
        string correlationId
    )
    {
        var actorUserId = ActorOf(msg);
        if (successor is { } successorId)
        {
            // Solo se anuncia un traspaso real: Communication rerutearía al sucesor sin serlo.
            var wasPrimary = customer.AssignedPreparerUserId == msg.UserId;
            if (customer.HandOverPreparer(msg.UserId, successorId, actorUserId).IsFailure || !wasPrimary)
                return false;
            await bus.PublishAsync(
                new CustomerPreparerAssignedIntegrationEvent
                {
                    TenantId = msg.TenantId,
                    CorrelationId = correlationId,
                    CustomerId = customer.Id,
                    PreparerUserId = successorId,
                    AssignedByUserId = actorUserId,
                }
            );
        }
        else
        {
            if (customer.UnassignPreparer(actorUserId).IsFailure)
                return false;
            await PublishUnassignedAsync(customer, msg, bus, correlationId);
        }

        return true;
    }

    private static async Task<bool> RevokeExtraAccessAsync(
        CustomerEntity customer,
        UserOffboardedIntegrationEvent msg,
        IMessageBus bus,
        string correlationId
    )
    {
        if (customer.RevokeAccess(msg.UserId, ActorOf(msg)).IsFailure)
            return false;
        return true;
    }

    // Archivado: no recibe sucesor (un archivado no admite preparador nuevo); solo se suelta al que se va.
    private static async Task<bool> ReleaseFromArchivedAsync(
        CustomerEntity customer,
        UserOffboardedIntegrationEvent msg,
        IMessageBus bus,
        string correlationId
    )
    {
        var released = customer.ReleaseOffboardedStaff(msg.UserId, ActorOf(msg));
        if (released.IsFailure)
            return false;
        if (released.Value)
            await PublishUnassignedAsync(customer, msg, bus, correlationId);
        return true;
    }

    private static ValueTask PublishUnassignedAsync(
        CustomerEntity customer,
        UserOffboardedIntegrationEvent msg,
        IMessageBus bus,
        string correlationId
    ) =>
        bus.PublishAsync(
            new CustomerPreparerUnassignedIntegrationEvent
            {
                TenantId = msg.TenantId,
                CorrelationId = correlationId,
                CustomerId = customer.Id,
                UnassignedByUserId = ActorOf(msg),
            }
        );

    private static Guid ActorOf(UserOffboardedIntegrationEvent msg) => msg.OffboardedByUserId ?? Guid.Empty;
}
