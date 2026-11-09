using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Categories;
using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Application.Requests.Building;
using TaxVision.Signature.Domain.Requests;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Requests.Commands.FromForm;

/// <summary>P-18 — arma la solicitud del comando de Tasks: firmantes, borrador, documentos y campo de firma.</summary>
public sealed class SignatureRequestFromFormBuilder(
    ITenantSignatureSettingsRepository settingsRepository,
    ISignatureCategoryResolver categoryResolver,
    ISignatureLimitsResolver limits,
    ISignaturePlatformSettingsProvider platform,
    ICustomerEmailProjectionRepository customerEmails
)
{
    // Completa el email (y el nombre si falta) de los firmantes dirigidos a un cliente: el comando llega
    // sin email y con CustomerId; se resuelve desde la proyección local. Null si algún cliente no resuelve.
    public async Task<IReadOnlyList<SignatureRequestFromFormSigner>?> ResolveSignersAsync(
        SignatureRequestFromFormRequestedIntegrationEvent command,
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

    /// <summary>Borrador con documentos, firmantes y su campo de firma. No guarda nada.</summary>
    public async Task<Result<SignatureRequest>> BuildAsync(
        SignatureRequestFromFormRequestedIntegrationEvent command,
        IReadOnlyList<SignatureRequestFromFormSigner> signers,
        IReadOnlyList<(SignatureRequestFromFormDocument Doc, VerifiableFile File)> validated,
        CancellationToken ct
    )
    {
        var policy = await limits.ResolveAsync(command.TenantId, ct);
        var maxFieldsPerSigner = (await platform.GetAsync(ct)).MaxFieldsPerSigner;
        var settings = await settingsRepository.GetByTenantIdAsync(command.TenantId, ct);
        var requestLimits = policy.ForRequests;

        // Categoría: canónica si es una conocida del tenant; si no, el texto crudo (se congela igual).
        var category = string.IsNullOrWhiteSpace(command.Category) ? "Client form" : command.Category!;
        var resolved = await categoryResolver.ResolveAsync(command.TenantId, category, ct);
        if (resolved.IsSuccess)
            category = resolved.Value;

        var draft = SignatureRequest.CreateDraft(
            limits: requestLimits,
            tenantId: command.TenantId,
            createdByUserId: command.CreatedByUserId,
            title: command.Title,
            description: null,
            category: category,
            tokenExpirationHours: policy.DefaultTokenHours,
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

        var added = SignatureRequestBuilder.AddDocuments(
            request,
            requestLimits,
            validated.Select(item => (item.Doc.FileId, command.Title))
        );
        if (added.IsFailure)
            return Result.Failure<SignatureRequest>(added.Error);

        var documentIds = added.Value;
        var placementIndex =
            command.Placement.DocumentIndex >= 0 && command.Placement.DocumentIndex < documentIds.Count
                ? command.Placement.DocumentIndex
                : 0;
        var placementDocumentId = documentIds[placementIndex];

        foreach (var signer in signers)
        {
            var identity = SignatureRequestBuilder.ParseSigner(signer.Email, signer.FullName, signer.Phone);
            if (identity.IsFailure)
                return Result.Failure<SignatureRequest>(identity.Error);

            var verification = ParseVerification(signer.VerificationMethod);

            // CustomerId EXPLÍCITO del comando (no se deduce por email): un prospecto queda sin cliente.
            var addedSigner = request.AddSigner(
                requestLimits,
                identity.Value.Email,
                identity.Value.FullName,
                signer.CustomerId,
                identity.Value.Phone,
                signer.Language,
                verification
            );
            if (addedSigner.IsFailure)
                return Result.Failure<SignatureRequest>(addedSigner.Error);

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
                maxFieldsPerSigner,
                addedSigner.Value.Id,
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
}
