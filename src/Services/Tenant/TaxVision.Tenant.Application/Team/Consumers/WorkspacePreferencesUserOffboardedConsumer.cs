using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;

namespace TaxVision.Tenant.Application.Team.Consumers;

/// <summary>
/// Retiro de un empleado: borra sus preferencias en esa oficina. La plantilla no se toca, no publica nada y el acuse
/// sale solo. Idempotente: una reentrega no encuentra filas. Si el borrado falla, sube y Wolverine reintenta.
/// </summary>
public static class WorkspacePreferencesUserOffboardedConsumer
{
    public static async Task Handle(
        UserOffboardedIntegrationEvent evt,
        IWorkspacePreferenceRepository preferences,
        ICorrelationContext correlation,
        ILogger<WorkspacePreference> logger,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var deleted = await preferences.DeleteAllOfUserAsync(evt.TenantId, evt.UserId, ct);
            logger.LogInformation(
                "Deleted {Count} workspace preferences of offboarded user {UserId} in tenant {TenantId}.",
                deleted,
                evt.UserId,
                evt.TenantId
            );
        }
    }
}
