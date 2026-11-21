using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using Wolverine;

namespace TaxVision.Tasks.Application.Files;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen. Tasks declara los suyos de esa oficina (adjuntos de tareas y
/// formularios, PDF de revisiones) con los mismos valores que la reposición manual; los ids ajenos se ignoran.
/// </summary>
public static class FileSourcesClaimConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        IFileSourceRedeclarer redeclarer,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        if (evt.TenantId == Guid.Empty || evt.FileIds.Count == 0)
            return;

        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
        {
            var declarations = await redeclarer.DeclarationsForAsync(
                evt.TenantId,
                evt.FileIds,
                correlation.CorrelationId,
                ct
            );
            foreach (var declaration in declarations)
                await bus.PublishAsync(declaration);
        }
    }
}
