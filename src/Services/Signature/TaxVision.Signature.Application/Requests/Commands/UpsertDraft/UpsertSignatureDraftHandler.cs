using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;

namespace TaxVision.Signature.Application.Requests.Commands.UpsertDraft;

/// <summary>
/// Autosave: reconcilia metadata + signers + fields en una sola transacción. Dividido en fases
/// explícitas por guardrail #1 (load → version → metadata → signers → fields → save).
/// </summary>
public static class UpsertSignatureDraftHandler
{
    public static async Task<Result<UpsertSignatureDraftResponse>> Handle(
        UpsertSignatureDraftCommand cmd,
        ISignatureRequestRepository repository,
        ICustomerEmailProjectionRepository customerProjection,
        ISignatureCategoryResolver categoryResolver,
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

        var metadata = await ApplyMetadata(request, cmd, categoryResolver, ct);
        if (metadata.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(metadata.Error);

        var signerIds = await ReconcileSigners(request, cmd.Signers, cmd.TenantId, customerProjection, ct);
        if (signerIds.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(signerIds.Error);

        var fields = ReconcileFields(request, cmd.Fields, signerIds.Value);
        if (fields.IsFailure)
            return Result.Failure<UpsertSignatureDraftResponse>(fields.Error);

        await unitOfWork.SaveChangesAsync(ct);
        await listCache.InvalidateAsync(cmd.TenantId, ct);
        return Result.Success(new UpsertSignatureDraftResponse(request.UpdatedAtUtc));
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
        ISignatureCategoryResolver categoryResolver,
        CancellationToken ct
    )
    {
        var category = await categoryResolver.ResolveAsync(cmd.TenantId, cmd.Category, ct);
        if (category.IsFailure)
            return Result.Failure(category.Error);

        var metadata = request.UpdateMetadata(cmd.Title, cmd.Description, category.Value, cmd.TokenExpirationHours);
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
            var applied = expOn ? request.EnableExpiration(cmd.TokenExpirationHours) : request.DisableExpiration();
            if (applied.IsFailure)
                return applied;
        }

        return Result.Success();
    }

    // ===== Fase 3: signers =====
    // Identidad: server Signer.Id. Entrantes con Id → existentes; sin Id → crear. Server sin eco → borrar.
    private static async Task<Result<IReadOnlyList<Guid>>> ReconcileSigners(
        SignatureRequest request,
        IReadOnlyList<DraftSignerSpec> inbound,
        Guid tenantId,
        ICustomerEmailProjectionRepository customerProjection,
        CancellationToken ct
    )
    {
        var resolvedIds = new Guid[inbound.Count];
        var keptIds = new HashSet<Guid>();

        for (var i = 0; i < inbound.Count; i++)
        {
            var spec = inbound[i];
            var vo = BuildSignerValueObjects(spec);
            if (vo.IsFailure)
                return Result.Failure<IReadOnlyList<Guid>>(vo.Error);

            if (spec.Id is { } existingId)
            {
                var existing = request.Signers.FirstOrDefault(s => s.Id == existingId);
                if (existing is null)
                    return Result.Failure<IReadOnlyList<Guid>>(
                        new Error("Signature.Request.SignerMissing", "Signer not found in this request.")
                    );

                var phone = request.SetSignerPhoneNumber(existing.Id, vo.Value.PhoneNumber);
                if (phone.IsFailure)
                    return Result.Failure<IReadOnlyList<Guid>>(phone.Error);
                var method = request.SetSignerRequiredVerificationMethod(existing.Id, spec.VerificationMethod);
                if (method.IsFailure)
                    return Result.Failure<IReadOnlyList<Guid>>(method.Error);

                resolvedIds[i] = existing.Id;
                keptIds.Add(existing.Id);
                continue;
            }

            var mappedCustomerId = await ResolveMappedCustomerId(tenantId, vo.Value.Email, customerProjection, ct);
            var added = request.AddSigner(
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

        return Result.Success<IReadOnlyList<Guid>>(resolvedIds);
    }

    // ===== Fase 4: fields =====
    // Identidad por Id; nuevos se colocan con PlaceField; los que ya no aparecen se borran.
    // Un cambio de posición exige mandar un field nuevo (Id null) y omitir el viejo.
    private static Result ReconcileFields(
        SignatureRequest request,
        IReadOnlyList<DraftFieldSpec> inbound,
        IReadOnlyList<Guid> signerIds
    )
    {
        var keptFieldIds = new HashSet<Guid>();

        foreach (var spec in inbound)
        {
            if (spec.SignerIndex < 0 || spec.SignerIndex >= signerIds.Count)
                return Result.Failure(
                    new Error(
                        "Signature.Request.FieldSignerIndexInvalid",
                        "Field references a signer not in the payload."
                    )
                );

            var signerId = signerIds[spec.SignerIndex];

            if (spec.Id is { } existingId)
            {
                var existing = request
                    .Signers.FirstOrDefault(s => s.Id == signerId)
                    ?.Fields.FirstOrDefault(f => f.Id == existingId);
                if (existing is null)
                    return Result.Failure(
                        new Error("Signature.Request.FieldMissing", "Field not found for the given signer.")
                    );

                keptFieldIds.Add(existingId);
                continue;
            }

            var position = FieldPosition.Create(spec.Page, spec.X, spec.Y, spec.Width, spec.Height);
            if (position.IsFailure)
                return position;
            var placed = request.PlaceField(signerId, spec.Kind, position.Value, spec.Label, spec.IsRequired);
            if (placed.IsFailure)
                return placed;
            keptFieldIds.Add(placed.Value.Id);
        }

        foreach (var signer in request.Signers)
        {
            foreach (var orphan in signer.Fields.Where(f => !keptFieldIds.Contains(f.Id)).Select(f => f.Id).ToList())
            {
                var removed = request.RemoveField(signer.Id, orphan);
                if (removed.IsFailure)
                    return removed;
            }
        }

        return Result.Success();
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
