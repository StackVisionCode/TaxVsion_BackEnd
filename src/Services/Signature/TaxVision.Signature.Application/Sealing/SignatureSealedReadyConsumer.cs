using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Cuando un archivo queda DISPONIBLE en CloudStorage y es el documento sellado de una solicitud
/// (coincide con su <c>SealedFileId</c>), emite el share-link público de descarga — ahora el archivo
/// ya está <c>Available</c>, así que no hay carrera — y publica
/// <see cref="SignatureReadyForDownloadIntegrationEvent"/> para que Notification mande el correo de
/// firma completada con el botón de descarga. Si el archivo disponible no es un sellado, no hace nada.
/// </summary>
public static class SignatureSealedReadyConsumer
{
    private static readonly TimeSpan DownloadLinkLifetime = TimeSpan.FromDays(90);

    public static async Task Handle(
        FileAvailableIntegrationEvent evt,
        ISignatureRequestRepository repository,
        ISignatureCloudStorageClient storage,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<SignatureRequest> logger,
        CancellationToken ct
    )
    {
        var correlationId = string.IsNullOrWhiteSpace(evt.CorrelationId)
            ? evt.EventId.ToString("N")
            : evt.CorrelationId;

        using (correlation.Push(correlationId))
        {
            var request = await repository.GetBySealedFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (request is null)
                return; // el archivo disponible no es un documento sellado

            var emails = request.Signers.Select(s => s.Email.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            var shareToken = await MintShareTokenAsync(request, evt.FileId, emails, storage, logger, ct);

            await bus.PublishAsync(
                new SignatureReadyForDownloadIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    SignatureRequestId = request.Id,
                    SealedFileId = evt.FileId,
                    CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
                    ShareToken = shareToken,
                    Signers = request
                        .Signers.Select(s => new SignerContactSnapshot(
                            s.Id,
                            s.Email.Value,
                            s.FullName.Value,
                            s.Language,
                            s.Order,
                            s.MappedCustomerId
                        ))
                        .ToList(),
                }
            );
        }
    }

    private static async Task<string?> MintShareTokenAsync(
        SignatureRequest request,
        Guid sealedFileId,
        IReadOnlyList<string> emails,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (emails.Count == 0)
            return null;

        var result = await storage.CreateDownloadShareLinkAsync(
            request.TenantId,
            sealedFileId,
            emails,
            DateTime.UtcNow.Add(DownloadLinkLifetime),
            ct
        );

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Download share link could not be created for request {RequestId}; the completion email will have no download button.",
                request.Id
            );
            return null;
        }

        return result.Value;
    }
}
