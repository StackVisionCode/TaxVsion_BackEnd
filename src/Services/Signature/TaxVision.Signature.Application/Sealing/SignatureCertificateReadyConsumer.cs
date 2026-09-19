using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Sealing;

/// <summary>
/// Cuando un archivo queda DISPONIBLE en CloudStorage y es el Certificate of Completion de una
/// solicitud (coincide con su <c>CertificateFileId</c>) Y la request pidió entregarlo
/// (<c>SendCertificateToSigners</c>), emite el share-link público del certificado y publica
/// <see cref="SignatureCertificateReadyForDownloadIntegrationEvent"/> para que Notification lo entregue
/// por el canal de cada firmante. Dispararse con el FileAvailable del propio certificado evita la
/// carrera de scan (el archivo ya está Available al mintear el link).
/// </summary>
public static class SignatureCertificateReadyConsumer
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
            var request = await repository.GetByCertificateFileIdAsync(evt.TenantId, evt.FileId, ct);
            if (request is null)
                return; // el archivo disponible no es el certificado de una request

            if (!request.SendCertificateToSigners)
                return; // la request no pidió entregar el certificado a los firmantes

            var emails = request.Signers.Select(s => s.Email.Value).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var shareToken =
                emails.Count == 0 ? null : await MintShareTokenAsync(request, evt.FileId, emails, storage, logger, ct);

            await bus.PublishAsync(
                new SignatureCertificateReadyForDownloadIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlationId,
                    SignatureRequestId = request.Id,
                    CertificateFileId = evt.FileId,
                    CompletedAtUtc = request.CompletedAtUtc ?? DateTime.UtcNow,
                    ShareToken = shareToken,
                    Signers = request
                        .Signers.Select(s => new SignerContactSnapshot(
                            s.Id,
                            s.Email.Value,
                            s.FullName.Value,
                            s.Language,
                            s.Order,
                            s.MappedCustomerId,
                            s.PhoneNumber?.Value,
                            SignerChannelResolver.PreferredChannelFor(s)
                        ))
                        .ToList(),
                }
            );
        }
    }

    private static async Task<string?> MintShareTokenAsync(
        SignatureRequest request,
        Guid certificateFileId,
        IReadOnlyList<string> emails,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        var result = await storage.CreateDownloadShareLinkAsync(
            request.TenantId,
            certificateFileId,
            emails,
            DateTime.UtcNow.Add(DownloadLinkLifetime),
            ct
        );

        if (result.IsFailure)
        {
            logger.LogWarning(
                "Certificate share link could not be created for request {RequestId}; the certificate delivery will have no download link.",
                request.Id
            );
            return null;
        }

        return result.Value;
    }
}
