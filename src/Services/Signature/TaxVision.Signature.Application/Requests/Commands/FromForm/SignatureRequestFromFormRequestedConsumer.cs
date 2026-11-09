using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.FromForm;

/// <summary>
/// P-18 — Signature crea una solicitud de firma desde un comando de Tasks (cola
/// signature-client-form-commands). Valida owner/carpeta y el hash esperado de la copia; crea la
/// solicitud con firmantes y campos; guarda ANTES de publicar; invoca Send si AutoSend. Idempotente
/// por ExternalReference. Un fallo de negocio se PUBLICA (…Failed), no se lanza (el reintento no ayuda).
/// Una copia que no estará nunca (rechazada, infectada, bloqueada, borrada) también se publica como Failed
/// al momento; una copia en vuelo (404 o aún escaneando) se reintenta con <see cref="CopyWaitCooldowns"/> y,
/// en el último intento, se publica Failed en vez de acabar en la cola de errores sin que Tasks se entere.
/// Un fallo de infraestructura (5xx, timeout) sigue el camino estándar: reintento y cola de errores.
/// </summary>
public static class SignatureRequestFromFormRequestedConsumer
{
    private const string OwnerTypeSignature = "Signature";
    private const string FolderTypeSignatures = "Signatures";
    private const string AvailableStatus = "Available";

    /// <summary>
    /// Espera de una copia en vuelo: un reintento por cada pausa; el intento que sigue a la última es el final.
    /// Program.cs registra con este mismo arreglo la política de <see cref="SignatureCopyNotReadyException"/>.
    /// </summary>
    public static readonly TimeSpan[] CopyWaitCooldowns =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(15),
    ];

    /// <summary>Número del último intento (Envelope.Attempts empieza en 1): el primero más un reintento por pausa.</summary>
    public static int FinalAttempt => CopyWaitCooldowns.Length + 1;

    // Estados de CloudStorage en los que el archivo ya no va a estar disponible.
    private static readonly string[] FinalUnavailableStatuses =
    [
        "Infected",
        "ScanFailed",
        "SoftDeleted",
        "BlockedByPolicy",
    ];

    public static async Task Handle(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        ISignatureRequestRepository repository,
        SignatureRequestFromFormBuilder builder,
        ISignatureCloudStorageClient storage,
        IRequestDocumentVerifier verifier,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        IFileMetadataRefRepository files,
        Envelope envelope,
        ILogger<SignatureRequestFromFormRequestedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(ResolveCorrelationId(command)))
        {
            // 1) Idempotencia por correlación de negocio: si ya existe, re-publicar Created y salir.
            var existing = await repository.GetByExternalReferenceAsync(
                command.TenantId,
                command.ExternalReference.SourceService,
                command.ExternalReference.SourceId,
                ct
            );
            if (existing is not null)
            {
                await PublishCreatedAsync(existing, command, correlation, bus);
                return;
            }

            // 2) Validar cada documento: debe estar disponible, ser de owner/carpeta de firma y casar el hash.
            var validated = new List<(SignatureRequestFromFormDocument Doc, VerifiableFile File)>(
                command.Documents.Count
            );
            foreach (var doc in command.Documents)
            {
                var lookup = await storage.GetFileAsync(command.TenantId, doc.FileId, ct);
                var availability = await ResolveAvailabilityAsync(command.TenantId, doc.FileId, lookup, files, ct);
                if (availability == CopyAvailability.LookupUnavailable)
                    // CloudStorage no respondió (5xx, timeout): camino estándar de reintento y cola de errores.
                    throw new InvalidOperationException(
                        $"Signature copy {doc.FileId} could not be looked up ({lookup.Error.Code}); retrying."
                    );

                var finalAttempt = envelope.Attempts >= FinalAttempt;
                if (
                    availability == CopyAvailability.Unavailable
                    || (availability == CopyAvailability.InFlight && finalAttempt)
                )
                {
                    logger.LogWarning(
                        "Signature copy {FileId} for form request {RequestId} is {Availability} (attempt {Attempt}); rejecting.",
                        doc.FileId,
                        command.RequestId,
                        availability,
                        envelope.Attempts
                    );
                    await PublishFailedAsync(
                        command,
                        SignatureRequestFromFormOutcomes.FileNotAvailable,
                        correlation,
                        bus
                    );
                    return;
                }

                if (availability == CopyAvailability.InFlight)
                    throw new SignatureCopyNotReadyException(
                        $"Signature copy {doc.FileId} is not available yet (attempt {envelope.Attempts} of {FinalAttempt}); retrying."
                    );

                var meta = lookup.Value;

                if (
                    !string.Equals(meta.OwnerType, OwnerTypeSignature, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(meta.FolderType, FolderTypeSignatures, StringComparison.OrdinalIgnoreCase)
                )
                {
                    logger.LogWarning(
                        "Signature copy {FileId} owner/folder mismatch ({Owner}/{Folder}); rejecting.",
                        doc.FileId,
                        meta.OwnerType,
                        meta.FolderType
                    );
                    await PublishFailedAsync(command, SignatureRequestFromFormOutcomes.OwnerMismatch, correlation, bus);
                    return;
                }

                if (!string.Equals(meta.ChecksumSha256, doc.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning("Signature copy {FileId} hash mismatch; rejecting.", doc.FileId);
                    await PublishFailedAsync(command, SignatureRequestFromFormOutcomes.HashMismatch, correlation, bus);
                    return;
                }

                validated.Add(
                    (doc, new VerifiableFile(meta.ChecksumSha256!, meta.ContentType ?? string.Empty, meta.SizeBytes))
                );
            }

            // 3) Resolver firmantes: un request dirigido a un cliente llega sin email (Tasks no lo guarda);
            // se resuelve aquí por CustomerId desde la proyección local de clientes.
            var signers = await builder.ResolveSignersAsync(command, ct);
            if (signers is null)
            {
                logger.LogWarning(
                    "Signature request from form {RequestId}: signer email could not be resolved for a customer.",
                    command.RequestId
                );
                await PublishFailedAsync(
                    command,
                    SignatureRequestFromFormOutcomes.SignerEmailUnavailable,
                    correlation,
                    bus
                );
                return;
            }

            // 4) Construir la solicitud (borrador + documentos con hash + firmantes + campos).
            var built = await builder.BuildAsync(command, signers, validated, ct);
            if (built.IsFailure)
            {
                logger.LogWarning(
                    "Signature request from form {RequestId} is invalid: {Code}.",
                    command.RequestId,
                    built.Error.Code
                );
                await PublishFailedAsync(command, SignatureRequestFromFormOutcomes.InvalidRequest, correlation, bus);
                return;
            }

            var request = built.Value;

            // 4b) Revisión del servidor de cada copia (PDF real, tamaño, páginas, firmas previas).
            var rejectedOrDeferred = await VerifyDocumentsAsync(request, validated, verifier, ct);
            if (rejectedOrDeferred == DocumentVerificationKind.Deferred)
                // No se pudo descargar todavía: reintentar por el inbox durable.
                throw new InvalidOperationException(
                    $"Signature copy for {command.RequestId} could not be verified yet; retrying."
                );
            if (rejectedOrDeferred == DocumentVerificationKind.Rejected)
            {
                logger.LogWarning(
                    "Signature request from form {RequestId}: a document was rejected.",
                    command.RequestId
                );
                await PublishFailedAsync(command, SignatureRequestFromFormOutcomes.DocumentRejected, correlation, bus);
                return;
            }

            // 5) Guardar ANTES de publicar (outbox durable).
            await repository.AddAsync(request, ct);
            try
            {
                await unitOfWork.SaveChangesAsync(ct);
            }
            catch (ConflictException)
            {
                // Dos entregas del mismo comando a la vez: gana una; la otra re-publica Created.
                var winner = await repository.GetByExternalReferenceAsync(
                    command.TenantId,
                    command.ExternalReference.SourceService,
                    command.ExternalReference.SourceId,
                    ct
                );
                if (winner is not null)
                {
                    await PublishCreatedAsync(winner, command, correlation, bus);
                    return;
                }
                throw;
            }

            await listCache.InvalidateAsync(command.TenantId, ct);
            await PublishCreatedAsync(request, command, correlation, bus);

            // 6) Envío automático: se publica por el outbox para que corra DESPUÉS del commit de este
            // consumer. Invocarlo en línea fallaba con NotFound porque el Send abre su propio scope y no
            // veía la solicitud recién guardada (aún sin confirmar la transacción Wolverine del consumer).
            if (command.AutoSend)
                await bus.PublishAsync(new SendSignatureRequestCommand(command.TenantId, request.Id));
        }
    }

    private enum CopyAvailability
    {
        Available,
        InFlight,
        Unavailable,
        LookupUnavailable,
    }

    // CloudStorage es la fuente; la proyección local aporta los resultados finales que ya anunció
    // (guardado rechazado, infección, bloqueo, scan fallido), que el lookup puede ver como 404.
    private static async Task<CopyAvailability> ResolveAvailabilityAsync(
        Guid tenantId,
        Guid fileId,
        Result<SignatureFileMetadata> lookup,
        IFileMetadataRefRepository files,
        CancellationToken ct
    )
    {
        if (lookup.IsSuccess)
        {
            var meta = lookup.Value;
            if (string.Equals(meta.Status, AvailableStatus, StringComparison.OrdinalIgnoreCase))
                return string.IsNullOrEmpty(meta.ChecksumSha256)
                    ? CopyAvailability.InFlight
                    : CopyAvailability.Available;
            if (FinalUnavailableStatuses.Contains(meta.Status, StringComparer.OrdinalIgnoreCase))
                return CopyAvailability.Unavailable;
        }

        var projection = await files.GetByFileIdAsync(tenantId, fileId, ct);
        if (projection is { HasScanOutcome: true } && projection.Status != FileScanStatus.Available)
            return CopyAvailability.Unavailable;

        if (lookup.IsSuccess || lookup.Error.Code == SignatureStorage.FileNotFoundCode)
            return CopyAvailability.InFlight;

        return CopyAvailability.LookupUnavailable;
    }

    // Verified si todas pasan; Rejected si alguna se rechaza; Deferred si alguna no se pudo revisar aún.
    private static async Task<DocumentVerificationKind> VerifyDocumentsAsync(
        SignatureRequest request,
        IReadOnlyList<(SignatureRequestFromFormDocument Doc, VerifiableFile File)> validated,
        IRequestDocumentVerifier verifier,
        CancellationToken ct
    )
    {
        foreach (var (doc, file) in validated)
        {
            var document = request.Documents.First(candidate => candidate.OriginalFileId == doc.FileId);
            var verification = await verifier.VerifyAsync(request, document.Id, file, ct);
            if (verification.Kind != DocumentVerificationKind.Verified)
                return verification.Kind;
        }
        return DocumentVerificationKind.Verified;
    }

    private static Task PublishCreatedAsync(
        SignatureRequest request,
        SignatureRequestFromFormRequestedIntegrationEvent command,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestFromFormCreatedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    RequestId = command.RequestId,
                    SignatureRequestId = request.Id,
                    ExternalReference = request.ExternalReference.ToMessaging(),
                }
            )
            .AsTask();

    private static Task PublishFailedAsync(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        string errorCode,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestFromFormFailedIntegrationEvent
                {
                    TenantId = command.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    RequestId = command.RequestId,
                    ErrorCode = errorCode,
                    ExternalReference =
                        new BuildingBlocks.Messaging.SignatureIntegrationEvents.SignatureExternalReference
                        {
                            SourceService = command.ExternalReference.SourceService,
                            SourceType = command.ExternalReference.SourceType,
                            SourceId = command.ExternalReference.SourceId,
                        },
                }
            )
            .AsTask();

    private static string ResolveCorrelationId(SignatureRequestFromFormRequestedIntegrationEvent command) =>
        string.IsNullOrWhiteSpace(command.CorrelationId) ? command.RequestId.ToString("N") : command.CorrelationId;
}
