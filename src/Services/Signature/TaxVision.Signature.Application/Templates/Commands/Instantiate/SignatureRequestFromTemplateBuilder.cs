using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Application.Profiles.EffectiveSignature;
using TaxVision.Signature.Application.Requests.Building;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;
using TaxVision.Signature.Domain.Templates;

namespace TaxVision.Signature.Application.Templates.Commands.Instantiate;

/// <summary>
/// Arma la solicitud a partir de una plantilla publicada, sin guardar nada: valida bindings y documentos, crea el
/// borrador con los defaults de la plantilla, y agrega firmantes, campos, audiencia, preparador y PIN.
/// </summary>
public sealed class SignatureRequestFromTemplateBuilder(
    ICustomerEmailProjectionRepository customerProjectionRepository,
    IEffectiveSignatureResolver effectiveResolver,
    ISignatureLimitsResolver limits,
    ISignaturePlatformSettingsProvider platform
)
{
    public async Task<Result<SignatureRequest>> BuildAsync(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template,
        CancellationToken ct
    )
    {
        var effectiveDocuments = ResolveEffectiveDocuments(cmd, template);
        if (effectiveDocuments.IsFailure)
            return Result.Failure<SignatureRequest>(effectiveDocuments.Error);

        var bindingValidation = ValidateBindings(cmd.SlotBindings, template);
        if (bindingValidation.IsFailure)
            return Result.Failure<SignatureRequest>(bindingValidation.Error);

        var signerVOs = await BuildSignerValueObjectsAsync(cmd, template, ct);
        if (signerVOs.IsFailure)
            return Result.Failure<SignatureRequest>(signerVOs.Error);

        var policy = (await limits.ResolveAsync(cmd.TenantId, ct)).ForRequests;
        var maxFieldsPerSigner = (await platform.GetAsync(ct)).MaxFieldsPerSigner;
        var requestResult = CreateDraft(cmd, template, policy);
        if (requestResult.IsFailure)
            return Result.Failure<SignatureRequest>(requestResult.Error);

        var request = requestResult.Value;
        var documentIds = AddDocuments(request, effectiveDocuments.Value, policy);
        if (documentIds.IsFailure)
            return Result.Failure<SignatureRequest>(documentIds.Error);

        var populated = PopulateSignersAndFields(
            request,
            template,
            signerVOs.Value,
            documentIds.Value,
            policy,
            maxFieldsPerSigner
        );
        if (populated.IsFailure)
            return Result.Failure<SignatureRequest>(populated.Error);

        // F7 — resolver audiencia Specific con los signerIds ya existentes.
        var audienceResult = ApplySpecificAudienceIfNeeded(request, template, populated.Value);
        if (audienceResult.IsFailure)
            return Result.Failure<SignatureRequest>(audienceResult.Error);

        var preparer = await InheritPreparerFieldsAsync(request, template, cmd, documentIds.Value, ct);
        if (preparer.IsFailure)
            return Result.Failure<SignatureRequest>(preparer.Error);

        // La plantilla puede traer un Practitioner PIN por defecto (hash ya calculado): se copia tal cual
        // a la solicitud (sin re-hashear). La request queda Draft aquí, así que SetPractitionerPin lo permite.
        if (template.PractitionerPinHash is { } templatePinHash)
        {
            var pinResult = request.SetPractitionerPin(templatePinHash, cmd.CreatedByUserId, DateTime.UtcNow);
            if (pinResult.IsFailure)
                return Result.Failure<SignatureRequest>(pinResult.Error);
        }

        return Result.Success(request);
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

    private sealed record EffectiveDocument(Guid TemplateDocumentId, Guid FileId, string Title);

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

    private async Task<Result<IReadOnlyList<SignerValueObjects>>> BuildSignerValueObjectsAsync(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template,
        CancellationToken ct
    )
    {
        var bindingsBySlot = cmd.SlotBindings.ToDictionary(b => b.SlotOrder);
        var signers = new List<SignerValueObjects>(template.Slots.Count);
        foreach (var slot in template.Slots.OrderBy(s => s.Order))
        {
            var binding = bindingsBySlot[slot.Order];
            var identity = SignatureRequestBuilder.ParseSigner(binding.Email, binding.FullName, binding.PhoneNumber);
            if (identity.IsFailure)
                return Result.Failure<IReadOnlyList<SignerValueObjects>>(identity.Error);

            var mapped = await customerProjectionRepository.FindActiveByEmailAsync(
                cmd.TenantId,
                identity.Value.Email.Value,
                ct
            );
            signers.Add(
                new SignerValueObjects(
                    slot.Order,
                    identity.Value.Email,
                    identity.Value.FullName,
                    mapped?.CustomerId,
                    slot.DefaultLanguage,
                    slot.RequiredVerificationMethod,
                    identity.Value.Phone
                )
            );
        }
        return Result.Success<IReadOnlyList<SignerValueObjects>>(signers);
    }

    private static Result<IReadOnlyList<EffectiveDocument>> ResolveEffectiveDocuments(
        CreateSignatureRequestFromTemplateCommand cmd,
        SignatureTemplate template
    )
    {
        var templateDocuments = template.Documents.OrderBy(document => document.Order).ToList();
        if (templateDocuments.Count == 0)
            return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                new Error("Signature.Template.NoDocument", "The template has no document to instantiate.")
            );

        var overrides = cmd.Documents ?? [];
        if (cmd.OriginalFileId is not null && overrides.Count > 0)
            return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                new Error(
                    "Signature.Template.DocumentOverrideConflict",
                    "Use either OriginalFileId for a legacy single-document template or Documents, not both."
                )
            );
        if (cmd.OriginalFileId is not null && templateDocuments.Count != 1)
            return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                new Error(
                    "Signature.Template.DocumentOverrideAmbiguous",
                    "OriginalFileId can only override a single-document template."
                )
            );
        if (overrides.GroupBy(item => item.TemplateDocumentId).Any(group => group.Count() > 1))
            return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                new Error(
                    "Signature.Template.DocumentOverrideDuplicate",
                    "Each template document can be overridden once."
                )
            );

        var templateDocumentIds = templateDocuments.Select(document => document.Id).ToHashSet();
        if (overrides.Any(item => !templateDocumentIds.Contains(item.TemplateDocumentId)))
            return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                new Error(
                    "Signature.Template.DocumentOverrideUnknown",
                    "A document override references an unknown template document."
                )
            );

        var overridesByDocument = overrides.ToDictionary(item => item.TemplateDocumentId);
        var result = new List<EffectiveDocument>(templateDocuments.Count);
        foreach (var document in templateDocuments)
        {
            overridesByDocument.TryGetValue(document.Id, out var documentOverride);
            var fileId = cmd.OriginalFileId ?? documentOverride?.OriginalFileId ?? document.FileId;
            if (fileId == Guid.Empty)
                return Result.Failure<IReadOnlyList<EffectiveDocument>>(
                    new Error("Signature.Template.DocumentFile", "Every instantiated document requires a file.")
                );

            var title = string.IsNullOrWhiteSpace(documentOverride?.Title)
                ? document.Title
                : documentOverride.Title.Trim();
            result.Add(new EffectiveDocument(document.Id, fileId, title));
        }

        return Result.Success<IReadOnlyList<EffectiveDocument>>(result);
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
        SignatureLimits limits
    )
    {
        // F7 — audiencia Specific se fija DESPUÉS (ApplySpecificAudienceIfNeeded) cuando los signerIds
        // ya existen. Para Create: si la plantilla es All usamos All; si es Specific aún no podemos,
        // así que creamos con sendPartial=false y lo activamos luego.
        var createWithPartialCopy =
            template.SendPartialCopyOnEachSignature && template.PartialCopyAudienceKind == PartialCopyAudienceKind.All;
        return SignatureRequest.CreateDraft(
            limits: limits,
            tenantId: cmd.TenantId,
            createdByUserId: cmd.CreatedByUserId,
            title: template.Title,
            description: cmd.DescriptionOverride ?? template.Description,
            category: template.Category,
            // Si el plan bajó después de publicar la plantilla, la vida del enlace se acota al techo actual.
            tokenExpirationHours: Math.Min(template.DefaultTokenExpirationHours, limits.MaxTokenHours),
            requiresSequentialSigning: template.RequiresSequentialSigning,
            requiresConsent: template.RequiresConsent,
            generateCertificate: template.GenerateCertificate,
            sendSealedDocumentToSigners: template.SendSealedDocumentToSigners,
            sendCertificateToSigners: template.SendCertificateToSigners,
            autoRemindersEnabled: template.AutoRemindersEnabled,
            reminderIntervalHours: template.ReminderIntervalHours,
            expirationEnabled: template.ExpirationEnabled,
            sendPartialCopyOnEachSignature: createWithPartialCopy,
            partialCopyAudience: createWithPartialCopy ? PartialCopyAudience.All() : null
        );
    }

    private static Result<IReadOnlyDictionary<Guid, Guid>> AddDocuments(
        SignatureRequest request,
        IReadOnlyList<EffectiveDocument> documents,
        SignatureLimits limits
    )
    {
        var added = SignatureRequestBuilder.AddDocuments(
            request,
            limits,
            documents.Select(document => (document.FileId, document.Title))
        );
        if (added.IsFailure)
            return Result.Failure<IReadOnlyDictionary<Guid, Guid>>(added.Error);

        return Result.Success<IReadOnlyDictionary<Guid, Guid>>(
            documents.Zip(added.Value).ToDictionary(pair => pair.First.TemplateDocumentId, pair => pair.Second)
        );
    }

    private static Result<IReadOnlyDictionary<int, Guid>> PopulateSignersAndFields(
        SignatureRequest request,
        SignatureTemplate template,
        IReadOnlyList<SignerValueObjects> signers,
        IReadOnlyDictionary<Guid, Guid> documentIds,
        SignatureLimits limits,
        int maxFieldsPerSigner
    )
    {
        var signerIdBySlotOrder = new Dictionary<int, Guid>(signers.Count);
        foreach (var signer in signers)
        {
            var addResult = request.AddSigner(
                limits,
                signer.Email,
                signer.FullName,
                signer.MappedCustomerId,
                phoneNumber: signer.PhoneNumber,
                language: signer.Language,
                requiredVerificationMethod: signer.RequiredVerificationMethod
            );
            if (addResult.IsFailure)
                return Result.Failure<IReadOnlyDictionary<int, Guid>>(addResult.Error);
            signerIdBySlotOrder[signer.SlotOrder] = addResult.Value.Id;
        }

        foreach (var field in template.Fields)
        {
            var signerId = signerIdBySlotOrder[field.SlotOrder];
            if (!documentIds.TryGetValue(field.TemplateDocumentId, out var documentId))
                return Result.Failure<IReadOnlyDictionary<int, Guid>>(
                    new Error(
                        "Signature.Template.FieldDocumentMissing",
                        "A template field references a missing document."
                    )
                );
            var placeResult = request.PlaceField(
                maxFieldsPerSigner,
                signerId,
                documentId,
                field.Kind,
                field.Position,
                field.Label,
                field.IsRequired,
                field.FontSizePt
            );
            if (placeResult.IsFailure)
                return Result.Failure<IReadOnlyDictionary<int, Guid>>(placeResult.Error);
        }
        return Result.Success<IReadOnlyDictionary<int, Guid>>(signerIdBySlotOrder);
    }

    /// <summary>
    /// F7 — si la plantilla pide audiencia Specific, mapea sus slotOrders a los signerIds reales recién
    /// creados y activa <c>SendSendPartialCopy(true, Specific(ids))</c>. Para All ya se activó en Create.
    /// Si la plantilla no pide copia parcial, no hace nada.
    /// </summary>
    private static Result ApplySpecificAudienceIfNeeded(
        SignatureRequest request,
        SignatureTemplate template,
        IReadOnlyDictionary<int, Guid> signerIdBySlotOrder
    )
    {
        if (!template.SendPartialCopyOnEachSignature)
            return Result.Success();
        if (template.PartialCopyAudienceKind != PartialCopyAudienceKind.Specific)
            return Result.Success();

        var signerIds = template
            .PartialCopyAudienceSlotOrders.Where(signerIdBySlotOrder.ContainsKey)
            .Select(order => signerIdBySlotOrder[order])
            .ToList();
        if (signerIds.Count == 0)
            return Result.Failure(
                new Error(
                    "Signature.Template.AudienceSlotsMissing",
                    "Template audience slot orders do not match any created signer."
                )
            );

        var audienceResult = PartialCopyAudience.Specific(signerIds);
        if (audienceResult.IsFailure)
            return Result.Failure(audienceResult.Error);

        return request.SetSendPartialCopy(true, audienceResult.Value);
    }

    /// <summary>
    /// Hereda los campos del preparador de la plantilla y fija la firma efectiva del usuario que instancia
    /// (su default si el tenant lo permite, o la de oficina). Sin firma efectiva no falla: el campo queda y
    /// el sellado cae al facsímil tipográfico del nombre del preparador.
    /// </summary>
    private async Task<Result> InheritPreparerFieldsAsync(
        SignatureRequest request,
        SignatureTemplate template,
        CreateSignatureRequestFromTemplateCommand cmd,
        IReadOnlyDictionary<Guid, Guid> documentIds,
        CancellationToken ct
    )
    {
        if (template.PreparerFields.Count == 0)
            return Result.Success();

        foreach (var field in template.PreparerFields)
        {
            if (!documentIds.TryGetValue(field.TemplateDocumentId, out var documentId))
                return Result.Failure(
                    new Error(
                        "Signature.Template.PreparerFieldDocumentMissing",
                        "A template preparer field references a missing document."
                    )
                );
            var placed = request.PlacePreparerField(documentId, field.Kind, field.Position, field.Label);
            if (placed.IsFailure)
                return placed;
        }

        var effective = await effectiveResolver.ResolveAsync(
            cmd.TenantId,
            cmd.CreatedByUserId,
            cmd.ActorCanSignOwn,
            ct
        );
        if (effective.IsSuccess)
            request.SetPreparerSignature(effective.Value.FileId);

        return Result.Success();
    }
}
