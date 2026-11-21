using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notes.Application.Notes.Abstractions;
using Wolverine;

namespace TaxVision.Notes.Application.Notes.Consumers;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen. Notes declara los adjuntos suyos de esa oficina con los
/// mismos valores que la reposición manual; los ids ajenos se ignoran en silencio.
/// </summary>
public static class NoteFileSourcesClaimConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        INoteFileSourceRedeclarer redeclarer,
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
