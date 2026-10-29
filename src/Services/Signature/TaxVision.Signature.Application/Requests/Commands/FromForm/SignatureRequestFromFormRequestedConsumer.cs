using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Messaging;
using TaxVision.Signature.Application.Requests.Commands.Send;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using Wolverine;

namespace TaxVision.Signature.Application.Requests.Commands.FromForm;

/// <summary>
/// P-18 — Signature crea una solicitud de firma desde un comando de Tasks (cola
/// signature-client-form-commands). Valida owner/carpeta y el hash esperado de la copia; crea la
/// solicitud con firmantes y campos; guarda ANTES de publicar; invoca Send si AutoSend. Idempotente
/// por ExternalReference. Un fallo de negocio se PUBLICA (…Failed), no se lanza (el reintento no ayuda);
/// un archivo aún no disponible SÍ se lanza para reintentar por el inbox durable.
/// </summary>
public static class SignatureRequestFromFormRequestedConsumer
{
    private const string OwnerTypeSignature = "Signature";
    private const string FolderTypeSignatures = "Signatures";
    private const string AvailableStatus = "Available";

    public static async Task Handle(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        ISignatureRequestRepository repository,
        ITenantSignatureSettingsRepository settingsRepository,
        ISignatureCategoryResolver categoryResolver,
        ISignatureCloudStorageClient storage,
        ICustomerEmailProjectionRepository customerEmails,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
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
            var validated = new List<(SignatureRequestFromFormDocument Doc, string Hash)>(command.Documents.Count);
            foreach (var doc in command.Documents)
            {
                var lookup = await storage.GetFileAsync(command.TenantId, doc.FileId, ct);
                if (lookup.IsFailure)
                    // La copia puede no estar disponible todavía: reintentar por el inbox durable.
                    throw new InvalidOperationException($"Signature copy {doc.FileId} is not available yet; retrying.");

                var meta = lookup.Value;
                if (
                    !string.Equals(meta.Status, AvailableStatus, StringComparison.OrdinalIgnoreCase)
                    || string.IsNullOrEmpty(meta.ChecksumSha256)
                )
                    throw new InvalidOperationException($"Signature copy {doc.FileId} is not available yet; retrying.");

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

                validated.Add((doc, meta.ChecksumSha256!));
            }

            // 3) Resolver firmantes: un request dirigido a un cliente llega sin email (Tasks no lo guarda);
            // se resuelve aquí por CustomerId desde la proyección local de clientes.
            var signers = await ResolveSignersAsync(command, customerEmails, ct);
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
            var built = await BuildRequestAsync(command, signers, validated, settingsRepository, categoryResolver, ct);
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

    // Completa el email (y el nombre si falta) de los firmantes dirigidos a un cliente: el comando llega
    // sin email y con CustomerId; se resuelve desde la proyección local. Null si algún cliente no resuelve.
    private static async Task<IReadOnlyList<SignatureRequestFromFormSigner>?> ResolveSignersAsync(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        ICustomerEmailProjectionRepository customerEmails,
        CancellationToken ct
    )
    {
        var resolved = new List<SignatureRequestFromFormSigner>(command.Signers.Count);
        foreach (var signer in command.Signers)
        {
            if (!string.IsNullOrWhiteSpace(signer.Email))
            {
                resolved.Add(signer);
                continue;
            }
            if (signer.CustomerId is not { } customerId)
                return null;
            var projection = await customerEmails.GetByCustomerIdAsync(command.TenantId, customerId, ct);
            if (projection is null || projection.IsArchived)
                return null;
            resolved.Add(
                signer with
                {
                    Email = projection.NormalizedEmail,
                    FullName = string.IsNullOrWhiteSpace(signer.FullName) ? projection.DisplayName : signer.FullName,
                }
            );
        }
        return resolved;
    }

    private static async Task<Result<SignatureRequest>> BuildRequestAsync(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        IReadOnlyList<SignatureRequestFromFormSigner> signers,
        IReadOnlyList<(SignatureRequestFromFormDocument Doc, string Hash)> validated,
        ITenantSignatureSettingsRepository settingsRepository,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);

        // Categoría: canónica si es una conocida del tenant; si no, el texto crudo (se congela igual).
        var category = string.IsNullOrWhiteSpace(command.Category) ? "Client form" : command.Category!;
        var resolved = await categoryResolver.ResolveAsync(command.TenantId, category, ct);
        if (resolved.IsSuccess)
            category = resolved.Value;

        var draft = SignatureRequest.CreateDraft(
            tenantId: command.TenantId,
            createdByUserId: command.CreatedByUserId,
            title: command.Title,
            description: null,
            category: category,
            tokenExpirationHours: settings?.DefaultTokenExpirationHoursValue
                ?? TenantSignatureSettings.DefaultTokenExpirationHours,
            requiresSequentialSigning: false,
            requiresConsent: settings?.RequiresConsentByDefault ?? true,
            generateCertificate: settings?.GenerateCertificateByDefault ?? true,
            sendSealedDocumentToSigners: settings?.SendSealedDocumentDefault ?? false,
            sendCertificateToSigners: settings?.SendCertificateToSignersDefault ?? false,
            autoRemindersEnabled: settings?.RemindersEnabledByDefault ?? true,
            reminderIntervalHours: settings?.DefaultReminderIntervalHoursValue
                ?? TenantSignatureSettings.DefaultReminderIntervalHours,
            expirationEnabled: settings?.ExpirationEnabledByDefault ?? true
        );
        if (draft.IsFailure)
            return draft;

        var request = draft.Value;
        request.AttachExternalReference(
            new ExternalReference(
                command.ExternalReference.SourceService,
                command.ExternalReference.SourceType,
                command.ExternalReference.SourceId
            )
        );

        var documentIds = new List<Guid>(validated.Count);
        foreach (var (doc, hash) in validated)
        {
            var add = request.AddDocument(doc.FileId, command.Title, null);
            if (add.IsFailure)
                return Result.Failure<SignatureRequest>(add.Error);

            var hashVo = DocumentHash.Create(hash);
            if (hashVo.IsFailure)
                return Result.Failure<SignatureRequest>(hashVo.Error);

            var attach = request.AttachDocumentHash(add.Value.Id, hashVo.Value);
            if (attach.IsFailure)
                return Result.Failure<SignatureRequest>(attach.Error);

            documentIds.Add(add.Value.Id);
        }

        var placementIndex =
            command.Placement.DocumentIndex >= 0 && command.Placement.DocumentIndex < documentIds.Count
                ? command.Placement.DocumentIndex
                : 0;
        var placementDocumentId = documentIds[placementIndex];

        foreach (var signer in signers)
        {
            var email = SignerEmail.Create(signer.Email);
            if (email.IsFailure)
                return Result.Failure<SignatureRequest>(email.Error);

            var fullName = SignerFullName.Create(signer.FullName);
            if (fullName.IsFailure)
                return Result.Failure<SignatureRequest>(fullName.Error);

            SignerPhoneNumber? phone = null;
            if (!string.IsNullOrWhiteSpace(signer.Phone))
            {
                var phoneResult = SignerPhoneNumber.Create(signer.Phone);
                if (phoneResult.IsFailure)
                    return Result.Failure<SignatureRequest>(phoneResult.Error);
                phone = phoneResult.Value;
            }

            var verification = ParseVerification(signer.VerificationMethod);

            // CustomerId EXPLÍCITO del comando (no se deduce por email): un prospecto queda sin cliente.
            var added = request.AddSigner(
                email.Value,
                fullName.Value,
                signer.CustomerId,
                phone,
                signer.Language,
                verification
            );
            if (added.IsFailure)
                return Result.Failure<SignatureRequest>(added.Error);

            var position = FieldPosition.Create(
                command.Placement.Page,
                command.Placement.X,
                command.Placement.Y,
                command.Placement.W,
                command.Placement.H
            );
            if (position.IsFailure)
                return Result.Failure<SignatureRequest>(position.Error);

            var field = request.PlaceField(
                added.Value.Id,
                placementDocumentId,
                SignatureFieldKind.Signature,
                position.Value,
                "Signature",
                isRequired: true
            );
            if (field.IsFailure)
                return Result.Failure<SignatureRequest>(field.Error);
        }

        return Result.Success(request);
    }

    private static SignerVerificationMethod? ParseVerification(string method) =>
        method?.Trim().ToLowerInvariant() switch
        {
            "smsotp" => SignerVerificationMethod.SmsOtp,
            "emailotp" => SignerVerificationMethod.EmailOtp,
            _ => null,
        };

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
