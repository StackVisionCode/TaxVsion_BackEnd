using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Application.Requests;
using TaxVision.Signature.Domain.Projections;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Templates;
using Wolverine;

namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Instancia una plantilla publicada. Fases explícitas por método privado:
/// <list type="number">
///   <item>Cargar plantilla y validar estado <c>Published</c>.</item>
///   <item>Validar bindings vs slots (1-1, sin sobrantes ni faltantes).</item>
///   <item>Construir value objects de todos los signers (fail-fast).</item>
///   <item>Promover a Ready si el archivo ya está disponible (misma lógica de <see cref="Requests.Commands.Create.CreateSignatureRequestHandler"/>).</item>
///   <item>Crear el aggregate <c>SignatureRequest</c>, agregar signers y campos.</item>
///   <item>Persistir y publicar <c>SignatureRequestCreated</c>.</item>
/// </list>
/// </summary>
public static class CreateSignatureRequestFromTemplateHandler
{
    public static async Task<Result<SignatureRequestResponse>> Handle(
        CreateSignatureRequestFromTemplateCommand cmd,
        ISignatureTemplateRepository templateRepository,
        ISignatureRequestRepository requestRepository,
        ICustomerEmailProjectionRepository customerProjectionRepository,
        IFileMetadataRefRepository fileRepository,
        IEffectiveSignatureResolver effectiveResolver,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var template = await templateRepository.GetByIdAsync(cmd.TenantId, cmd.TemplateId, ct);
        if (template is null)
            return Failure("Signature.Template.NotFound", "The signature template does not exist for this tenant.");
        if (template.Status != SignatureTemplateStatus.Published)
            return Failure("Signature.Template.NotPublished", "Only published templates can be instantiated.");

        // P7: el documento viene del caller o, si no, del documento base de la plantilla.
        var originalFileId = ResolveOriginalFileId(cmd, template);
        if (originalFileId is null)
            return Failure(
                "Signature.Template.NoDocument",
                "No document was provided and the template has no base document to reuse."
            );

        var bindingValidation = ValidateBindings(cmd.SlotBindings, template);
        if (bindingValidation.IsFailure)
            return Result.Failure<SignatureRequestResponse>(bindingValidation.Error);

        var signerVOs = await BuildSignerValueObjectsAsync(cmd, template, customerProjectionRepository, ct);
        if (signerVOs.IsFailure)
            return Result.Failure<SignatureRequestResponse>(signerVOs.Error);

        var requestResult = CreateDraft(cmd, template, originalFileId.Value);
        if (requestResult.IsFailure)
            return Result.Failure<SignatureRequestResponse>(requestResult.Error);

        var request = requestResult.Value;
        var populated = PopulateSignersAndFields(request, template, signerVOs.Value);
        if (populated.IsFailure)
            return Result.Failure<SignatureRequestResponse>(populated.Error);

        var preparer = await InheritPreparerFieldsAsync(request, template, cmd, effectiveResolver, ct);
        if (preparer.IsFailure)
            return Result.Failure<SignatureRequestResponse>(preparer.Error);

        // La plantilla puede traer un Practitioner PIN por defecto (hash ya calculado): se copia tal cual
        // a la solicitud (sin re-hashear). La request queda Draft aquí, así que SetPractitionerPin lo permite.
        if (template.PractitionerPinHash is { } templatePinHash)
        {
            var pinResult = request.SetPractitionerPin(templatePinHash, cmd.CreatedByUserId, DateTime.UtcNow);
            if (pinResult.IsFailure)
                return Result.Failure<SignatureRequestResponse>(pinResult.Error);
        }

        await TryPromoteToReadyIfFileAvailable(request, cmd.TenantId, originalFileId.Value, fileRepository, ct);
        await requestRepository.AddAsync(request, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        await PublishCreatedEventAsync(request, template, correlation, bus);

        return Result.Success(SignatureRequestResponse.From(request));
    }

    private sealed record SignerValueObjects(
        int SlotOrder,
        SignerEmail Email,
        SignerFullName FullName,
        Guid? MappedCustomerId,
        string Language,
        Domain.Requests.SignerVerificationMethod? RequiredVerificationMethod,
        SignerPhoneNumber? PhoneNumber
    );

    // ============== Fase 2: validar bindings ==============

    private static Result ValidateBindings(IReadOnlyList<SlotBinding> bindings, SignatureTemplate template)
    {
        if (bindings.Count != template.Slots.Count)
            return Result.Failure(
                new Error(
                    "Signature.Template.BindingMismatch",
                    $"Expected {template.Slots.Count} bindings but received {bindings.Count}."
                )
            );

        var slotOrders = template.Slots.Select(s => s.Order).ToHashSet();
        var seen = new HashSet<int>(bindings.Count);
        foreach (var binding in bindings)
        {
            if (!slotOrders.Contains(binding.SlotOrder))
                return Result.Failure(
                    new Error(
                        "Signature.Template.BindingUnknownSlot",
                        $"Binding references unknown slot order {binding.SlotOrder}."
                    )
                );
            if (!seen.Add(binding.SlotOrder))
                return Result.Failure(
                    new Error(
                        "Signature.Template.BindingDuplicate",
                        $"Binding duplicates slot order {binding.SlotOrder}."
                    )
                );
        }
        return Result.Success();
    }

    // ============== Fase 3: construir VOs por slot ==============

    private static async Task<Result<IReadOnlyList<SignerValueObjects>>> BuildSignerValueObjectsAsync(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template,
        ICustomerEmailProjectionRepository customerProjectionRepository,
        CancellationToken ct
    )
    {
        var bindingsBySlot = cmd.SlotBindings.ToDictionary(b => b.SlotOrder);
        var signers = new List<SignerValueObjects>(template.Slots.Count);
        foreach (var slot in template.Slots.OrderBy(s => s.Order))
        {
            var binding = bindingsBySlot[slot.Order];
            var emailResult = SignerEmail.Create(binding.Email);
            if (emailResult.IsFailure)
                return Result.Failure<IReadOnlyList<SignerValueObjects>>(emailResult.Error);
            var nameResult = SignerFullName.Create(binding.FullName);
            if (nameResult.IsFailure)
                return Result.Failure<IReadOnlyList<SignerValueObjects>>(nameResult.Error);

            SignerPhoneNumber? phone = null;
            if (!string.IsNullOrWhiteSpace(binding.PhoneNumber))
            {
                var phoneResult = SignerPhoneNumber.Create(binding.PhoneNumber);
                if (phoneResult.IsFailure)
                    return Result.Failure<IReadOnlyList<SignerValueObjects>>(phoneResult.Error);
                phone = phoneResult.Value;
            }

            var mapped = await FindMappedCustomerAsync(
                cmd.TenantId,
                emailResult.Value,
                customerProjectionRepository,
                ct
            );
            signers.Add(
                new SignerValueObjects(
                    slot.Order,
                    emailResult.Value,
                    nameResult.Value,
                    mapped,
                    slot.DefaultLanguage,
                    slot.RequiredVerificationMethod,
                    phone
                )
            );
        }
        return Result.Success<IReadOnlyList<SignerValueObjects>>(signers);
    }

    private static async Task<Guid?> FindMappedCustomerAsync(
        Guid tenantId,
        SignerEmail email,
        ICustomerEmailProjectionRepository repo,
        CancellationToken ct
    )
    {
        var match = await repo.FindActiveByEmailAsync(tenantId, email.Value, ct);
        return match?.CustomerId;
    }

    // ============== Fase 4: factory del aggregate ==============

    /// <summary>Documento efectivo: override del caller si vino, si no el base de la plantilla.</summary>
    private static Guid? ResolveOriginalFileId(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template
    )
    {
        if (cmd.OriginalFileId is { } provided && provided != Guid.Empty)
            return provided;
        return template.BaseDocumentFileId is { } baseId && baseId != Guid.Empty ? baseId : null;
    }

    /// <summary>
    /// Crea la solicitud con TODOS los defaults de la plantilla (P7 + defaults de entrega/recordatorio).
    /// La plantilla es la fuente explícita: sus toggles de entregar documento firmado/certificado y de
    /// auto-recordatorios se copian tal cual a la solicitud (antes los recordatorios salían de tenant
    /// settings y los toggles de entrega ni se aplicaban).
    /// </summary>
    private static Result<SignatureRequest> CreateDraft(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template,
        Guid originalFileId
    ) =>
        SignatureRequest.CreateDraft(
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: template.Title,
            description: cmd.DescriptionOverride ?? template.Description,
            category: template.Category,
            originalFileId: originalFileId,
            tokenExpirationHours: template.DefaultTokenExpirationHours,
            requiresSequentialSigning: template.RequiresSequentialSigning,
            requiresConsent: template.RequiresConsent,
            generateCertificate: template.GenerateCertificate,
            sendSignedDocumentToSigners: template.SendSignedDocumentToSigners,
            sendCertificateToSigners: template.SendCertificateToSigners,
            autoRemindersEnabled: template.AutoRemindersEnabled,
            reminderIntervalHours: template.ReminderIntervalHours
        );

    // ============== Fase 5: agregar signers y campos ==============

    private static Result PopulateSignersAndFields(
        SignatureRequest request,
        SignatureTemplate template,
        IReadOnlyList<SignerValueObjects> signers
    )
    {
        var signerIdBySlotOrder = new Dictionary<int, Guid>(signers.Count);
        foreach (var signer in signers)
        {
            var addResult = request.AddSigner(
                signer.Email,
                signer.FullName,
                signer.MappedCustomerId,
                language: signer.Language,
                requiredVerificationMethod: signer.RequiredVerificationMethod
            );
            if (addResult.IsFailure)
                return Result.Failure(addResult.Error);
            signerIdBySlotOrder[signer.SlotOrder] = addResult.Value.Id;
        }

        foreach (var field in template.Fields)
        {
            var signerId = signerIdBySlotOrder[field.SlotOrder];
            var placeResult = request.PlaceField(signerId, field.Kind, field.Position, field.Label, field.IsRequired);
            if (placeResult.IsFailure)
                return Result.Failure(placeResult.Error);
        }
        return Result.Success();
    }

    /// <summary>
    /// Hereda los campos del preparador de la plantilla y fija la firma efectiva del usuario que instancia
    /// (su default si el tenant lo permite, o la de oficina). Sin firma efectiva no falla: el campo queda y
    /// el sellado cae al facsímil tipográfico del nombre del preparador.
    /// </summary>
    private static async Task<Result> InheritPreparerFieldsAsync(
        SignatureRequest request,
        SignatureTemplate template,
        CreateSignatureRequestFromTemplateCommand cmd,
        IEffectiveSignatureResolver effectiveResolver,
        CancellationToken ct
    )
    {
        if (template.PreparerFields.Count == 0)
            return Result.Success();

        foreach (var field in template.PreparerFields)
        {
            var placed = request.PlacePreparerField(field.Kind, field.Position, field.Label);
            if (placed.IsFailure)
                return placed;
        }

        var effective = await effectiveResolver.ResolveAsync(cmd.TenantId, cmd.CreatedByUserId, ct);
        if (effective.IsSuccess)
            request.SetPreparerSignature(effective.Value.FileId);

        return Result.Success();
    }

    // ============== Fase 6: promoción a Ready + publicar ==============
    // Reutiliza la misma lógica que la creación directa: si el archivo ya está
    // disponible se promueve a Ready; caso contrario espera el FileAvailable consumer.

    private static async Task TryPromoteToReadyIfFileAvailable(
        SignatureRequest request,
        Guid tenantId,
        Guid originalFileId,
        IFileMetadataRefRepository fileRepository,
        CancellationToken ct
    )
    {
        var file = await fileRepository.GetByFileIdAsync(tenantId, originalFileId, ct);
        if (file is null || file.Status != FileScanStatus.Available)
            return;
        if (string.IsNullOrEmpty(file.ChecksumSha256))
            return;

        var hashResult = DocumentHash.Create(file.ChecksumSha256);
        if (hashResult.IsFailure)
            return;

        request.MarkReadyForSending(hashResult.Value);
    }

    private static Task PublishCreatedEventAsync(
        SignatureRequest request,
        SignatureTemplate template,
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

    // ============== Helpers ==============

    private static Result<SignatureRequestResponse> Failure(string code, string message) =>
        Result.Failure<SignatureRequestResponse>(new Error(code, message));
}
