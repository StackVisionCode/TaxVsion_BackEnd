using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Signature.Application.Abstractions;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: Signature declara solo los de sus solicitudes que estén
/// en la lista, con los mismos valores que el backfill manual. Los ids ajenos se ignoran. Sale por outbox.
/// </summary>
public static class FileSourcesClaimRequestedConsumer
{
    public static async Task Handle(
        FileSourcesClaimRequestedIntegrationEvent evt,
        ISignatureOriginalFileQuery files,
        ISignatureTemplateFileQuery templates,
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
            var claimed = evt.FileIds.Take(FileSourcesClaimRequestedIntegrationEvent.MaxFileIds).ToHashSet();
            var requests = await files.ListOwningFilesAsync(evt.TenantId, claimed, ct);
            if (requests.Count == 0)
                return;

            var originals = requests.Select(SignatureFileDeclarations.OriginalsOf).ToList();
            var templateFiles = await templates.TemplateBaseFilesAsync(
                evt.TenantId,
                [.. originals.SelectMany(request => request.FileIds).Where(claimed.Contains)],
                ct
            );
            var declarations = requests
                .Where(SignatureFileDeclarations.HasSealedFiles)
                .SelectMany(request => SignatureFileDeclarations.SealedOutputs(request, correlation.CorrelationId))
                .Concat(
                    originals.SelectMany(request =>
                        SignatureFileDeclarations.Originals(
                            evt.TenantId,
                            request,
                            templateFiles,
                            correlation.CorrelationId
                        )
                    )
                )
                .Where(declaration => claimed.Contains(declaration.FileId));
            foreach (var declaration in declarations)
                await bus.PublishAsync(declaration);
        }
    }
}
