using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.CustomerIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Employees;
using Wolverine;

namespace TaxVision.Customer.Application.Employees.Consumers;

/// <summary>
/// Al RETIRAR (offboard) a un empleado: (1) marca el directorio como retirado — deja de ser preparador
/// elegible; (2) traspasa sus clientes PRIMARY al sucesor (o los desasigna si no hay) para que la oficina
/// siga trabajándolos; y (3) le revoca los accesos ADICIONALES (no-primary): se va, no debe conservar
/// ningún acceso. Idempotente; por lotes (keyset). Publica por cliente para que Communication (preparador)
/// y las proyecciones de visibilidad (asignación) reruteen.
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

        // 1. Directorio: retirado (terminal) — nunca vuelve a ser preparador elegible.
        await directory.MarkOffboardedAsync(msg.UserId, ct);
        await unitOfWork.SaveChangesAsync(ct);

        // 2. ¿Hay un sucesor que sea preparador elegible en ESTE tenant? La invariante
        // "el preparador es un empleado activo" la valida el handler, no el aggregate; aqui
        // se resuelve una sola vez. Si no es elegible (o es el mismo que se retira), se desasigna
        // en vez de asignar a un preparador invalido — la oficina retoma la cola sin dueño.
        Guid? successor = null;
        if (msg.SuccessorUserId is { } candidate && candidate != msg.UserId)
        {
            var entry = await directory.GetByUserIdAsync(candidate, ct);
            if (entry is not null && entry.TenantId == msg.TenantId && entry.IsEligiblePreparer)
                successor = candidate;
        }

        // 3. Reasignar (al sucesor) o desasignar (si no hay) sus clientes, por lotes.
        var actorUserId = msg.OffboardedByUserId ?? Guid.Empty;
        var afterId = Guid.Empty;
        var total = 0;
        while (true)
        {
            var batch = await customers.ListByAssignedPreparerAsync(msg.TenantId, msg.UserId, BatchSize, afterId, ct);
            if (batch.Count == 0)
                break;

            foreach (var customer in batch)
            {
                if (successor is { } successorId)
                {
                    // Handover: el sucesor queda primary y el saliente pierde la fila (no la conserva degradada).
                    if (customer.HandOverPreparer(msg.UserId, successorId, actorUserId).IsFailure)
                        continue;
                    await bus.PublishAsync(
                        new CustomerPreparerAssignedIntegrationEvent
                        {
                            TenantId = msg.TenantId,
                            CorrelationId = correlation.CorrelationId,
                            CustomerId = customer.Id,
                            PreparerUserId = successorId,
                            AssignedByUserId = actorUserId,
                        }
                    );
                }
                else
                {
                    if (customer.UnassignPreparer(actorUserId).IsFailure)
                        continue;
                    await bus.PublishAsync(
                        new CustomerPreparerUnassignedIntegrationEvent
                        {
                            TenantId = msg.TenantId,
                            CorrelationId = correlation.CorrelationId,
                            CustomerId = customer.Id,
                            UnassignedByUserId = actorUserId,
                        }
                    );
                }
                await bus.PublishAsync(CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId));
                total++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            afterId = batch[^1].Id; // keyset avanza siempre → sin bucle infinito aunque algún handover falle.
            if (batch.Count < BatchSize)
                break;
        }

        // 4. Revocar los accesos ADICIONALES (no-primary) del saliente: se va, no conserva ningún acceso.
        var revokedExtra = 0;
        afterId = Guid.Empty;
        while (true)
        {
            var batch = await customers.ListNonPrimaryAssignedAsync(msg.TenantId, msg.UserId, BatchSize, afterId, ct);
            if (batch.Count == 0)
                break;

            foreach (var customer in batch)
            {
                if (customer.RevokeAccess(msg.UserId, actorUserId).IsFailure)
                    continue;
                await bus.PublishAsync(CustomerAssignmentSnapshot.From(customer, correlation.CorrelationId));
                revokedExtra++;
            }

            await unitOfWork.SaveChangesAsync(ct);
            afterId = batch[^1].Id; // keyset avanza siempre; las filas revocadas ya no matchean.
            if (batch.Count < BatchSize)
                break;
        }

        if (total > 0 || revokedExtra > 0)
            logger.LogInformation(
                "Offboarded {UserId} in tenant {TenantId}: {Primary} primary client(s) handed over/unassigned, "
                    + "{Extra} extra access(es) revoked.",
                msg.UserId,
                msg.TenantId,
                total,
                revokedExtra
            );
    }
}
