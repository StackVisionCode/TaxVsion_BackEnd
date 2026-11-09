using BuildingBlocks.Common;
using BuildingBlocks.Messaging;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Application.Projections;

/// <summary>
/// La oficina guardó su perfil: los correos que firma pasan a llevar el nombre comercial y la dirección
/// postal en el pie. Ordenado por <c>Version</c> (una igual o menor se ignora) e idempotente. Sin fila aún
/// (llegó antes que <c>TenantCreated</c> o el backfill) se reintenta: el perfil se aplica cuando la fila exista.
/// </summary>
public static class TenantProfileChangedConsumer
{
    public static async Task Handle(
        TenantProfileChangedIntegrationEvent evt,
        ITenantProfileRefRepository repository,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ILogger<TenantProfileRef> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var existing = await repository.GetByTenantIdAsync(evt.ProfileTenantId, ct);
            if (existing is null)
                throw new DependencyNotReadyException("Tenant", $"profile v{evt.Version} of {evt.ProfileTenantId}");

            var postalAddress = evt.ResolvePostalAddress()?.ToSingleLine();
            if (!existing.ApplyProfile(evt.Version, evt.DisplayName, postalAddress, DateTime.UtcNow))
            {
                logger.LogInformation(
                    "Tenant {TenantId} profile version {Version} ignored (stale).",
                    evt.ProfileTenantId,
                    evt.Version
                );
                return;
            }

            await unitOfWork.SaveChangesAsync(ct);
            logger.LogInformation(
                "Tenant {TenantId} profile version {Version} applied in the Scribe directory.",
                evt.ProfileTenantId,
                evt.Version
            );
        }
    }
}
