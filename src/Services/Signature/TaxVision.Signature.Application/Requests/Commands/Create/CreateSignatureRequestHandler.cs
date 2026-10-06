using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.Create;

/// <summary>
/// Fases: (1) factory del aggregate, (2) si el archivo ya está disponible en CloudStorage
/// se adjunta el hash original, (3) persistir, (4) publicar el evento de creación.
/// La disponibilidad del documento es un flag derivado — no mueve el Status.
/// </summary>
public static class CreateSignatureRequestHandler
{
    public static async Task<Result<SignatureRequestResponse>> Handle(
        CreateSignatureRequestCommand cmd,
        ISignatureRequestRepository repository,
        IFileMetadataRefRepository fileRepository,
        ITenantSignatureSettingsRepository settingsRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        ISignatureCategoryResolver categoryResolver,
        ISignatureCloudStorageClient storage,
        ILogger<CreateSignatureRequestCommand> logger,
        CancellationToken ct
    )
    {
        // La categoría debe ser de sistema o una custom del tenant; se guarda con su nombre canónico.
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure<SignatureRequestResponse>(category.Error);

        // Overrides del preparador o, si no mandó valor, defaults del tenant.
        var settings = await settingsRepository.GetByTenantIdAsync(cmd.TenantId, ct);
        var remindersEnabled = cmd.AutoRemindersEnabled ?? settings?.RemindersEnabledByDefault ?? true;
        var reminderInterval =
            cmd.ReminderIntervalHours
            ?? settings?.DefaultReminderIntervalHoursValue
            ?? TenantSignatureSettings.DefaultReminderIntervalHours;
        // F7 — defaults de entrega.
        var sendSealed = cmd.SendSealedDocumentToSigners ?? settings?.SendSealedDocumentDefault ?? false;
        var sendPartial = cmd.SendPartialCopyOnEachSignature ?? settings?.SendPartialCopyDefault ?? false;
        var expirationEnabled = cmd.ExpirationEnabled ?? settings?.ExpirationEnabledByDefault ?? true;

        var draftResult = CreateDraft(
            cmd,
            category.Value,
            remindersEnabled,
            reminderInterval,
            sendSealed,
            sendPartial,
            expirationEnabled
        );
        if (draftResult.IsFailure)
            return Result.Failure<SignatureRequestResponse>(draftResult.Error);

        var request = draftResult.Value;
        await TryAttachHashIfFileAvailable(request, cmd, fileRepository, storage, logger, ct);
        await PersistRequestAsync(request, repository, unitOfWork, ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        await PublishCreatedEventAsync(request, cmd, correlation, bus);

        return Result.Success(SignatureRequestResponse.From(request));
    }

    // ============== Fase 1: factory del aggregate ==============

    private static Result<SignatureRequest> CreateDraft(
        CreateSignatureRequestCommand cmd,
        string category,
        bool autoRemindersEnabled,
        int reminderIntervalHours,
        bool sendSealed,
        bool sendPartial,
        bool expirationEnabled
    ) =>
        SignatureRequest.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: cmd.Title,
            description: cmd.Description,
            category: category,
            originalFileId: cmd.OriginalFileId,
            tokenExpirationHours: cmd.TokenExpirationHours,
            requiresSequentialSigning: cmd.RequiresSequentialSigning,
            requiresConsent: cmd.RequiresConsent,
            generateCertificate: cmd.GenerateCertificate,
            sendSealedDocumentToSigners: sendSealed,
            sendCertificateToSigners: cmd.SendCertificateToSigners,
            autoRemindersEnabled: autoRemindersEnabled,
            reminderIntervalHours: reminderIntervalHours,
            expirationEnabled: expirationEnabled,
            sendPartialCopyOnEachSignature: sendPartial,
            partialCopyAudience: cmd.PartialCopyAudience
        );

    // ============== Fase 2: adjuntar hash si el archivo ya está disponible ==============
    // Cache-aside: la proyección local se alimenta por bus (FileAvailable). Si está Available
    // la usamos directa; si no, consultamos la fuente autoritaria por HTTP y resincronizamos
    // la proyección. Esto desbloquea casos donde el bus perdió el evento o la proyección
    // quedó stale (p.ej. Deleted mientras el file sigue vivo en CloudStorage).
    private static async Task TryAttachHashIfFileAvailable(
        SignatureRequest request,
        CreateSignatureRequestCommand cmd,
        IFileMetadataRefRepository fileRepository,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        var hash = ExtractAvailableHash(file);

        if (hash is null)
        {
            // Proyección vacía o no-Available: pregunta a CloudStorage (fuente autoritaria).
            hash = await ResyncFromCloudStorageAsync(cmd, fileRepository, storage, logger, ct);
            if (hash is null)
                return;
        }

        var hashResult = Domain.Requests.ValueObjects.DocumentHash.Create(hash);
        if (hashResult.IsFailure)
            return;

        request.AttachOriginalHash(hashResult.Value);
    }

    private static string? ExtractAvailableHash(FileMetadataRef? file) =>
        file is not null && file.Status == FileScanStatus.Available && !string.IsNullOrEmpty(file.ChecksumSha256)
            ? file.ChecksumSha256
            : null;

    // Pide el file a CloudStorage. Si está Available, upserta la proyección local y devuelve
    // el checksum. Si CloudStorage no responde o el file no está Available, devuelve null —
    // el consumer de FileAvailable lo adjuntará cuando el bus entregue.
    private static async Task<string?> ResyncFromCloudStorageAsync(
        CreateSignatureRequestCommand cmd,
        IFileMetadataRefRepository fileRepository,
        ISignatureCloudStorageClient storage,
        ILogger logger,
        CancellationToken ct
    )
    {
        var lookup = await storage.GetFileAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (lookup.IsFailure)
        {
            logger.LogInformation(
                "Hash lookup for {FileId} fell through to bus (CloudStorage: {Error}).",
                cmd.OriginalFileId,
                lookup.Error.Message
            );
            return null;
        }

        var metadata = lookup.Value;
        if (!string.Equals(metadata.Status, "Available", StringComparison.OrdinalIgnoreCase))
            return null;
        if (string.IsNullOrEmpty(metadata.ChecksumSha256))
            return null;

        await UpsertProjectionAsync(cmd, fileRepository, metadata, ct);
        return metadata.ChecksumSha256;
    }

    private static async Task UpsertProjectionAsync(
        CreateSignatureRequestCommand cmd,
        IFileMetadataRefRepository fileRepository,
        SignatureFileMetadata metadata,
        CancellationToken ct
    )
    {
        var existing = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (existing is null)
        {
            var projection = FileMetadataRef.ForAvailable(
                cmd.TenantId,
                cmd.OriginalFileId,
                metadata.ObjectKey ?? string.Empty,
                metadata.ContentType ?? string.Empty,
                metadata.SizeBytes,
                metadata.ChecksumSha256!
            );
            await fileRepository.AddAsync(projection, ct);
        }
        else
        {
            existing.MarkAvailable(
                metadata.ObjectKey ?? existing.ObjectKey,
                metadata.ContentType ?? existing.ContentType,
                metadata.SizeBytes,
                metadata.ChecksumSha256!
            );
        }
    }

    // ============== Fase 3: persistir ==============

    private static async Task PersistRequestAsync(
        SignatureRequest request,
        ISignatureRequestRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        await repository.AddAsync(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
    }

    // ============== Fase 4: publicar evento ==============

    private static Task PublishCreatedEventAsync(
        SignatureRequest request,
        CreateSignatureRequestCommand cmd,
        ICorrelationContext correlation,
        IMessageBus bus
    ) =>
        bus.PublishAsync(
                new SignatureRequestCreatedIntegrationEvent
                {
                    TenantId = request.TenantId,
                    CorrelationId = correlation.CorrelationId,
                    SignatureRequestId = request.Id,
                    CreatedByUserId = request.CreatedByUserId,
                    Title = request.Title,
                    Category = request.Category.ToString(),
                    OriginalFileId = request.OriginalFileId,
                    TokenExpirationHours = request.TokenExpirationHours,
                    RequiresSequentialSigning = request.RequiresSequentialSigning,
                    SignerCount = request.Signers.Count,
                    ExpiresAtUtc = request.ExpiresAtUtc,
                }
            )
            .AsTask();
}
