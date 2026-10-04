using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
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
        CancellationToken ct
    )
    {
        // La categoría debe ser de sistema o una custom del tenant; se guarda con su nombre canónico.
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure<SignatureRequestResponse>(category.Error);

        // La política de recordatorio: override del preparador o, si no lo mandó, el default del tenant.
        var settings = await settingsRepository.GetByTenantIdAsync(cmd.TenantId, ct);
        var remindersEnabled = cmd.AutoRemindersEnabled ?? settings?.RemindersEnabledByDefault ?? true;
        var reminderInterval =
            cmd.ReminderIntervalHours
            ?? settings?.DefaultReminderIntervalHoursValue
            ?? TenantSignatureSettings.DefaultReminderIntervalHours;

        var draftResult = CreateDraft(cmd, category.Value, remindersEnabled, reminderInterval);
        if (draftResult.IsFailure)
            return Result.Failure<SignatureRequestResponse>(draftResult.Error);

        var request = draftResult.Value;
        await TryAttachHashIfFileAvailable(request, cmd, fileRepository, ct);
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
        int reminderIntervalHours
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
            sendSignedDocumentToSigners: cmd.SendSignedDocumentToSigners,
            sendCertificateToSigners: cmd.SendCertificateToSigners,
            autoRemindersEnabled: autoRemindersEnabled,
            reminderIntervalHours: reminderIntervalHours
        );

    // ============== Fase 2: adjuntar hash si el archivo ya está disponible ==============
    // Si FileAvailable ya llegó, adjuntamos el hash ahora. Si no, el consumer lo hará.
    // En ambos casos la solicitud sigue en Draft hasta que el preparador decida enviar.
    private static async Task TryAttachHashIfFileAvailable(
        SignatureRequest request,
        CreateSignatureRequestCommand cmd,
        IFileMetadataRefRepository fileRepository,
        CancellationToken ct
    )
    {
        var file = await fileRepository.GetByFileIdAsync(cmd.TenantId, cmd.OriginalFileId, ct);
        if (file is null || file.Status != FileScanStatus.Available)
            return;

        if (string.IsNullOrEmpty(file.ChecksumSha256))
            return;

        var hashResult = Domain.Requests.ValueObjects.DocumentHash.Create(file.ChecksumSha256);
        if (hashResult.IsFailure)
            return;

        request.AttachOriginalHash(hashResult.Value);
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
