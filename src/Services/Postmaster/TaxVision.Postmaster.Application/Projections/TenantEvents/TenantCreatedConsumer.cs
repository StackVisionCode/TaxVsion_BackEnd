using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Application.Projections.TenantEvents;

/// <summary>
/// Oficina nueva ⇒ se guarda su nombre, que es lo que Postmaster pone en el From cuando un correo de
/// esa oficina acaba saliendo por el proveedor del sistema en su nombre.
///
/// <para>Se reusa <c>TenantCreatedIntegrationEvent</c>, que ya trae <c>Name</c> y <c>SubDomain</c> y
/// que otros cinco servicios ya proyectan igual, en vez de pedirle a Tenant un evento nuevo: el dato
/// ya cruza el bus y añadir un segundo evento con la misma información solo daría dos versiones de la
/// verdad que pueden desincronizarse.</para>
///
/// <para>Idempotente por <c>UpsertAsync</c>: este evento lo reparte el bus a varios consumers y
/// Wolverine puede reentregarlo, así que tiene que poder correr dos veces sin romper.</para>
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantDirectoryRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantDirectoryEntry> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            await repository.UpsertAsync(evt.NewTenantId, evt.Name, evt.SubDomain, DateTime.UtcNow, ct);
            await unitOfWork.SaveChangesAsync(ct);

            logger.LogInformation(
                "Tenant {TenantId} projected into the Postmaster directory as '{Name}'.",
                evt.NewTenantId,
                evt.Name
            );
        }
    }
}
