using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>
/// Oficina nueva ⇒ se guarda su nombre, que es lo que el layout <c>tenant-base</c> pone en la cabecera
/// (cuando no hay logo) y en el pie de cada correo que la oficina firma.
///
/// <para>Se reusa <c>TenantCreatedIntegrationEvent</c>, que ya trae <c>Name</c> y <c>SubDomain</c> y
/// que varios servicios ya proyectan igual, en vez de pedir un evento nuevo.</para>
///
/// <para>Idempotente por <c>UpsertAsync</c>: el bus lo reparte a varios consumers y puede reentregarlo.</para>
/// </summary>
public static class TenantCreatedConsumer
{
    public static async Task Handle(
        TenantCreatedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProfileRef> logger,
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
                "Tenant {TenantId} projected into the Scribe directory as '{Name}'.",
                evt.NewTenantId,
                evt.Name
            );
        }
    }
}
