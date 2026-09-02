using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>Resultado de la re-asignacion: cuantas solicitudes se miraron y cuantos archivos se re-asignaron.</summary>
public sealed record ReassignedSealedOwnersReport(bool DryRun, int RequestsScanned, int FilesReassigned);

/// <summary>
/// Migracion (una sola vez): re-asigna en CloudStorage el dueno de los documentos firmados
/// EXISTENTES de un tenant, del OwnerType=Signature (como se guardaban antes del fix) al cliente
/// firmante, para que aparezcan bajo su carpeta "Signed Documents" en Documents. Solo re-asigna
/// cuando hay un unico cliente mapeado (misma regla que los sellados nuevos, ver
/// <see cref="SealedDocumentOwner"/>). Con <paramref name="DryRun"/> solo cuenta, sin publicar.
/// </summary>
public sealed record ReassignSealedDocumentOwnersCommand(Guid TenantId, bool DryRun);

public static class ReassignSealedDocumentOwnersHandler
{
    public static async Task<Result<ReassignedSealedOwnersReport>> Handle(
        ReassignSealedDocumentOwnersCommand command,
        ISignatureRequestRepository requests,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var completed = await requests.ListCompletedWithSealedFileAsync(command.TenantId, ct);
        var reassigned = 0;

        foreach (var request in completed)
        {
            var (ownerType, ownerId) = SealedDocumentOwner.Resolve(
                request.Signers.Select(signer => signer.MappedCustomerId).ToList(),
                request.Id
            );

            // Solo re-asignamos cuando el sellado pertenece claramente a un cliente. Con 0/varios
            // firmantes-cliente se deja como esta (OwnerType=Signature).
            if (ownerType != "Customer")
                continue;

            foreach (var fileId in new[] { request.SealedFileId, request.CertificateFileId })
            {
                if (fileId is not { } id)
                    continue;

                if (!command.DryRun)
                {
                    await bus.PublishAsync(
                        new ReassignFileOwnerRequestedIntegrationEvent
                        {
                            TenantId = command.TenantId,
                            FileId = id,
                            NewOwnerType = ownerType,
                            NewOwnerId = ownerId,
                            ActorId = request.CreatedByUserId,
                            CorrelationId = correlation.CorrelationId,
                        }
                    );
                }
                reassigned++;
            }
        }

        return Result.Success(new ReassignedSealedOwnersReport(command.DryRun, completed.Count, reassigned));
    }
}
