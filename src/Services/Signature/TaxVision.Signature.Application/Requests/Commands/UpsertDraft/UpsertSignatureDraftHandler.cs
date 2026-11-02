using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Requests.Commands.UpsertDraft;

/// <summary>
/// Autosave y guardado final de un borrador: reconcilia el estado completo (metadata + documentos +
/// firmantes + campos) en una sola transacción. Fases explícitas por guardrail #1 (load → version →
/// metadata → documents → signers → fields → save). Idempotente: un documento sin id se reconoce por su
/// archivo y un firmante sin id por su email, así un reintento (o una respuesta perdida) no duplica.
/// </summary>
public static class UpsertSignatureDraftHandler
{
    public static async Task<Result<UpsertSignatureDraftResponse>> Handle(
        UpsertSignatureDraftCommand cmd,
        ISignatureRequestRepository repository,
        ICustomerEmailProjectionRepository customerProjection,
        ISignatureCategoryResolver categoryResolver,
        ISignatureLimitsResolver limits,
        ISignaturePlatformSettingsProvider platform,
        IUnitOfWork unitOfWork,
        ISignatureRequestListCacheInvalidator listCache,
        CancellationToken ct
    )
    {
        var request = await repository.GetByIdAsync(cmd.TenantId, cmd.SignatureRequestId, ct);
        if (request is null)
            return NotFound();

        var versionCheck = EnsureNoVersionConflict(request, cmd.ExpectedUpdatedAtUtc);
        if (versionCheck.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(versionCheck.Error);

        var policy = (await limits.ResolveAsync(cmd.TenantId, ct)).ForRequests;
        var metadata = await ApplyMetadata(request, cmd, policy, categoryResolver, ct);
        if (metadata.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(metadata.Error);

        var documentIds = ReconcileDocuments(request, cmd.Documents, policy);
        if (documentIds.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(documentIds.Error);

        var signerIds = await ReconcileSigners(request, cmd.Signers, cmd.TenantId, customerProjection, policy, ct);
        if (signerIds.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(signerIds.Error);

        var maxFieldsPerSigner = (await platform.GetAsync(ct)).MaxFieldsPerSigner;
        var fieldIds = ReconcileFields(request, cmd.Fields, signerIds.Value, documentIds.Value, maxFieldsPerSigner);
        if (fieldIds.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(fieldIds.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success(
            new UpsertSignatureDraftResponse(
                request.UpdatedAtUtc,
                cmd.Documents.Select(spec => new UpsertedDraftDocument(spec.LocalId, documentIds.Value[spec.LocalId]))
                    .ToList(),
                signerIds.Value,
                fieldIds.Value
            )
        );
    }

    // ===== Fase 1: versión =====
    // El cliente envía el UpdatedAtUtc que vio; si el server avanzó, otra pestaña ganó.
    private static Result EnsureNoVersionConflict(SignatureRequest request, DateTime? expected)
    {
        if (expected is null)
            return Result.Success();
        if (request.UpdatedAtUtc == expected.Value)
            return Result.Success();
        return Result.Failure(
            new Error(
                "Signature.Request.VersionConflict",
                "The draft was modified in another session. Reload to see the latest version."
            )
        );
    }

    // ===== Fase 2: metadata =====
    private static async Task<Result> ApplyMetadata(
        SignatureRequest request,
        UpsertSignatureDraftCommand cmd,
        SignatureLimits policy,
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure(category.Error);

        var metadata = request.UpdateMetadata(
            policy,
            cmd.Title,
            cmd.Description,
            category.Value,
            cmd.TokenExpirationHours
        );
        if (metadata.IsFailure)
            return metadata;

        if (cmd.SendSealedDocumentToSigners is { } signedDelivery)
        {
            var applied = request.SetSealedDocumentDelivery(signedDelivery);
            if (applied.IsFailure)
                return applied;
        }

        if (cmd.SendCertificateToSigners is { } certDelivery)
        {
            var applied = request.SetCertificateDelivery(certDelivery);
            if (applied.IsFailure)
                return applied;
        }

        if (cmd.CertificateGenerationMode is { } certificateMode)
        {
            var applied = request.SetCertificateGenerationMode(certificateMode);
            if (applied.IsFailure)
                return applied;
        }

        if (cmd.AutoRemindersEnabled is { } reminders)
        {
            var applied = request.SetReminderPolicy(
                reminders,
                cmd.ReminderIntervalHours ?? request.ReminderIntervalHours
            );
            if (applied.IsFailure)
                return applied;
        }

        // F7 — copia parcial (requiere audiencia si ON).
        if (cmd.SendPartialCopyOnEachSignature is { } partialOn)
        {
            var applied = request.SetSendPartialCopy(partialOn, cmd.PartialCopyAudience);
            if (applied.IsFailure)
                return applied;
        }

        // F7 — expiración opcional: ON exige horas, OFF borra el reloj.
        if (cmd.ExpirationEnabled is { } expOn)
        {
            var applied = expOn ? request.EnableExpiration(policy, cmd.TokenExpirationHours) : request.DisableExpiration();
            if (applied.IsFailure)
                return applied;
        }

        return Result.Success();
    }

    // ===== Fase 3: documents =====
    private static Result<IReadOnlyDictionary<string, Guid>> ReconcileDocuments(
        SignatureRequest request,
        IReadOnlyList<DraftDocumentSpec> inbound,
        SignatureLimits policy
    )
    {
        var documentCount = policy.CheckDocumentCount(inbound?.Count ?? 0);
        if (documentCount.IsFailure)
            return Result.Failure<IReadOnlyDictionary<string, Guid>>(documentCount.Error);

        var resolvedIds = new Dictionary<string, Guid>(StringComparer.Ordinal);
        var keptIds = new HashSet<Guid>();
        var orderedIds = new List<Guid>(inbound.Count);
        // Los que el payload nombra por id no se pueden tomar por archivo.
        var referencedIds = inbound.Where(spec => spec.Id is not null).Select(spec => spec.Id!.Value).ToHashSet();

        foreach (var spec in inbound)
        {
            if (string.IsNullOrWhiteSpace(spec.LocalId) || !resolvedIds.TryAdd(spec.LocalId, Guid.Empty))
                return Result.Failure<IReadOnlyDictionary<string, Guid>>(
                    new Error("Signature.Request.DocumentLocalId", "Every document must have a unique local id.")
                );

            // Sin id: el mismo archivo ya en la solicitud es ese documento (un guardado anterior lo creó).
            var document = spec.Id is { } existingId
                ? request.Documents.FirstOrDefault(candidate => candidate.Id == existingId)
                : request.Documents.FirstOrDefault(candidate =>
                    candidate.OriginalFileId == spec.OriginalFileId
                    && !keptIds.Contains(candidate.Id)
                    && !referencedIds.Contains(candidate.Id)
                );
            if (spec.Id is not null && document is null)
                return Result.Failure<IReadOnlyDictionary<string, Guid>>(
                    new Error("Signature.Request.DocumentMissing", "Document not found in this request.")
                );

            if (document is not null)
            {
                if (document.OriginalFileId != spec.OriginalFileId)
                {
                    var replace = request.ReplaceDocumentFile(document.Id, spec.OriginalFileId, spec.PageCount);
                    if (replace.IsFailure)
                        return Result.Failure<IReadOnlyDictionary<string, Guid>>(replace.Error);
                }

                if (!string.Equals(document.Title, spec.Title.Trim(), StringComparison.Ordinal))
                {
                    var rename = request.RenameDocument(document.Id, spec.Title);
                    if (rename.IsFailure)
                        return Result.Failure<IReadOnlyDictionary<string, Guid>>(rename.Error);
                }
            }
            else
            {
                var added = request.AddDocument(policy, spec.OriginalFileId, spec.Title, spec.Note, spec.PageCount);
                if (added.IsFailure)
                    return Result.Failure<IReadOnlyDictionary<string, Guid>>(added.Error);
                document = added.Value;
            }

            resolvedIds[spec.LocalId] = document.Id;
            keptIds.Add(document.Id);
            orderedIds.Add(document.Id);
        }

        foreach (
            var documentId in request
                .Documents.Select(document => document.Id)
                .Where(id => !keptIds.Contains(id))
                .ToList()
        )
        {
            var removed = request.RemoveDocument(documentId);
            if (removed.IsFailure)
                return Result.Failure<IReadOnlyDictionary<string, Guid>>(removed.Error);
        }

        var reordered = request.ReorderDocuments(orderedIds);
        return reordered.IsFailure
            ? Result.Failure<IReadOnlyDictionary<string, Guid>>(reordered.Error)
            : Result.Success<IReadOnlyDictionary<string, Guid>>(resolvedIds);
    }

    // ===== Fase 4: signers =====
    // Identidad: server Signer.Id; sin Id, el firmante con ese email (un guardado anterior lo creó); si no
    // hay, se crea. Server sin eco → borrar. Al final, el orden de firma es el del payload.
    private static async Task<Result<IReadOnlyList<Guid>>> ReconcileSigners(
        SignatureRequest request,
        IReadOnlyList<DraftSignerSpec> inbound,
        Guid tenantId,
        ICustomerEmailProjectionRepository customerProjection,
        SignatureLimits policy,
        CancellationToken ct
    )
    {
        var resolvedIds = new Guid[inbound.Count];
        var keptIds = new HashSet<Guid>();
        // Los que el payload nombra por id no se pueden tomar por email.
        var referencedIds = inbound.Where(spec => spec.Id is not null).Select(spec => spec.Id!.Value).ToHashSet();

        for (var i = 0; i < inbound.Count; i++)
        {
            var spec = inbound[i];
            var vo = BuildSignerValueObjects(spec);
            if (vo.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(vo.Error);

            var existing = spec.Id is { } existingId
                ? request.Signers.FirstOrDefault(s => s.Id == existingId)
                : request.Signers.FirstOrDefault(s =>
                    !keptIds.Contains(s.Id)
                    && !referencedIds.Contains(s.Id)
                    && string.Equals(s.Email.Value, vo.Value.Email.Value, StringComparison.OrdinalIgnoreCase)
                );
            if (spec.Id is not null && existing is null)
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error("Signature.Request.SignerMissing", "Signer not found in this request.")
                );

            if (existing is not null)
            {
                var phone = request.SetSignerPhoneNumber(existing.Id, vo.Value.PhoneNumber);
                if (phone.IsFailure)
                    return Result.Failure<IReadOnlyList<Guid>>(phone.Error);
                var method = request.SetSignerRequiredVerificationMethod(policy, existing.Id, spec.VerificationMethod);
                if (method.IsFailure)
                    return Result.Failure<IReadOnlyList<Guid>>(method.Error);

                resolvedIds[i] = existing.Id;
                keptIds.Add(existing.Id);
                continue;
            }

            var mappedCustomerId = await ResolveMappedCustomerId(tenantId, vo.Value.Email, customerProjection, ct);
            var added = request.AddSigner(
                policy,
                vo.Value.Email,
                vo.Value.FullName,
                mappedCustomerId,
                vo.Value.PhoneNumber,
                spec.Language,
                spec.VerificationMethod
            );
            if (added.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(added.Error);

            resolvedIds[i] = added.Value.Id;
            keptIds.Add(added.Value.Id);
        }

        foreach (var toRemove in request.Signers.Select(s => s.Id).Where(id => !keptIds.Contains(id)).ToList())
        {
            var removed = request.RemoveSigner(toRemove);
            if (removed.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(removed.Error);
        }

        if (resolvedIds.Length > 0 && !request.Signers.Select(s => s.Id).SequenceEqual(resolvedIds))
        {
            var reordered = request.ReorderSigners(resolvedIds);
            if (reordered.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(reordered.Error);
        }

        return Result.Success<IReadOnlyList<Guid>>(resolvedIds);
    }

    // ===== Fase 5: fields =====
    // Identidad por Id; nuevos se colocan con PlaceField; los que ya no aparecen se borran.
    // Un cambio de posición exige mandar un field nuevo (Id null) y omitir el viejo.
    // Devuelve los ids en el orden del payload.
    private static Result<IReadOnlyList<Guid>> ReconcileFields(
        SignatureRequest request,
        IReadOnlyList<DraftFieldSpec> inbound,
        IReadOnlyList<Guid> signerIds,
        IReadOnlyDictionary<string, Guid> documentIds,
        int maxFieldsPerSigner
    )
    {
        var keptFieldIds = new HashSet<Guid>();
        var fieldIds = new List<Guid>(inbound.Count);

        foreach (var spec in inbound)
        {
            if (spec.SignerIndex < 0 || spec.SignerIndex >= signerIds.Count)
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error(
                        "Signature.Request.FieldSignerIndexInvalid",
                        "Field references a signer not in the payload."
                    )
                );

            var signerId = signerIds[spec.SignerIndex];
            if (!documentIds.TryGetValue(spec.DocumentLocalId, out var documentId))
                return Result.Failure<IReadOnlyList<Guid>>(
                    new Error(
                        "Signature.Request.FieldDocumentLocalIdInvalid",
                        "Field references a document not in the payload."
                    )
                );

            if (spec.Id is { } existingId)
            {
                var existing = request
                    .Signers.FirstOrDefault(s => s.Id == signerId)
                    ?.Fields.FirstOrDefault(f => f.Id == existingId);
                if (existing is null)
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Signature.Request.FieldMissing", "Field not found for the given signer.")
                    );
                if (existing.DocumentId != documentId)
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error(
                            "Signature.Request.FieldDocumentMismatch",
                            "An existing field cannot be moved to another document."
                        )
                    );

                keptFieldIds.Add(existingId);
                fieldIds.Add(existingId);
                continue;
            }

            var position = FieldPosition.Create(spec.Page, spec.X, spec.Y, spec.Width, spec.Height);
            if (position.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(position.Error);
            var placed = request.PlaceField(
                maxFieldsPerSigner,
                signerId,
                documentId,
                spec.Kind,
                position.Value,
                spec.Label,
                spec.IsRequired,
                spec.FontSizePt
            );
            if (placed.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(placed.Error);
            keptFieldIds.Add(placed.Value.Id);
            fieldIds.Add(placed.Value.Id);
        }

        foreach (var signer in request.Signers)
        {
            foreach (var orphan in signer.Fields.Where(f => !keptFieldIds.Contains(f.Id)).Select(f => f.Id).ToList())
            {
                var removed = request.RemoveField(signer.Id, orphan);
                if (removed.IsFailure)
                    return Result.Failure<IReadOnlyList<Guid>>(removed.Error);
            }
        }

        return Result.Success<IReadOnlyList<Guid>>(fieldIds);
    }

    // ===== Helpers =====

    private sealed record SignerVO(SignerEmail Email, SignerFullName FullName, SignerPhoneNumber? PhoneNumber);

    private static Result<SignerVO> BuildSignerValueObjects(DraftSignerSpec spec)
    {
        var email = SignerEmail.Create(spec.Email);
        if (email.IsFailure)
            return Result.Failure<SignerVO>(email.Error);
        var name = SignerFullName.Create(spec.FullName);
        if (name.IsFailure)
            return Result.Failure<SignerVO>(name.Error);

        SignerPhoneNumber? phone = null;
        if (!string.IsNullOrWhiteSpace(spec.PhoneNumber))
        {
            var p = SignerPhoneNumber.Create(spec.PhoneNumber);
            if (p.IsFailure)
                return Result.Failure<SignerVO>(p.Error);
            phone = p.Value;
        }
        return Result.Success(new SignerVO(email.Value, name.Value, phone));
    }

    private static async Task<Guid?> ResolveMappedCustomerId(
        Guid tenantId,
        SignerEmail email,
        ICustomerEmailProjectionRepository projection,
        CancellationToken ct
    )
    {
        var match = await projection.FindActiveByEmailAsync(tenantId, email.Value, ct);
        return match?.CustomerId;
    }

    private static Result<UpsertSignatureDraftResponse> NotFound() =>
        Result.Failure<UpsertSignatureDraftResponse>(
            new Error("Signature.Request.NotFound", "The signature request does not exist for this tenant.")
        );
}
