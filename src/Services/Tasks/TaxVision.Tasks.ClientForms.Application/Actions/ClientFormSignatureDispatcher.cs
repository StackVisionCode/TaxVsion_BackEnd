using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Messaging.SignatureIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// P-18 — Cuando el Submission Summary termina con una copia de firma, prepara el comando a Signature:
/// resuelve el firmante (el participante del formulario), arma la ubicación desde el ancla y marca la
/// revisión Requesting. No guarda ni publica: el consumer que lo invoca guarda antes de publicar.
/// </summary>
public sealed class ClientFormSignatureDispatcher(
    IFormTemplateVersionRepository versions,
    IFormEngine engine,
    IFormParticipantRepository participants,
    ICorrelationContext correlation,
    ILogger<ClientFormSignatureDispatcher> logger
)
{
    private const string PurposeSignatures = "Signatures";

    public async Task<object?> PrepareAsync(
        FormRequest request,
        DocumentGenerationCompletedIntegrationEvent evt,
        CancellationToken ct
    )
    {
        if (evt.ExternalReferenceId is not { } generationId || evt.SignatureAnchor is null)
            return null;

        var copy = evt.Copies.FirstOrDefault(c =>
            string.Equals(c.Purpose, PurposeSignatures, StringComparison.Ordinal)
        );
        if (copy is null || string.IsNullOrEmpty(copy.ContentHash))
            return null;

        var revisionNumber = request.RevisionNumberForGeneration(generationId);
        if (revisionNumber is not { } revision)
            return null;

        // Idempotencia: si ya se pidió la firma de esta revisión, no re-disparar en una reentrega.
        if (request.RevisionHasPendingSignature(revision))
            return null;

        var signer = await ResolveSignerAsync(request, revision, ct);
        if (signer is null)
            return null;

        var verification = await ResolveVerificationAsync(request, ct);

        var marked = request.MarkSignatureRequesting(revision, copy.FileId, copy.ContentHash!);
        if (marked.IsFailure)
            return null;

        var sourceId = ClientFormSignatureReference.BuildSourceId(request.Id, revision);
        var createdByUserId = request.ResponsibleUserId ?? request.RequestedByUserId;

        return new SignatureRequestFromFormRequestedIntegrationEvent
        {
            TenantId = request.TenantId,
            CorrelationId = correlation.CorrelationId,
            RequestId = Guid.NewGuid(),
            IdempotencyKey = sourceId,
            ExternalReference = new SignatureExternalReference
            {
                SourceService = ClientFormSignatureReference.SourceService,
                SourceType = ClientFormSignatureReference.SourceType,
                SourceId = sourceId,
            },
            CreatedByUserId = createdByUserId,
            Title = request.Title,
            Category = null,
            Documents =
            [
                new SignatureRequestFromFormDocument { FileId = copy.FileId, ExpectedSha256 = copy.ContentHash! },
            ],
            Signers =
            [
                new SignatureRequestFromFormSigner
                {
                    Email = signer.Email,
                    FullName = signer.FullName,
                    Phone = signer.Phone,
                    Language = signer.Language,
                    VerificationMethod = verification,
                    CustomerId = signer.CustomerId,
                },
            ],
            Placement = new SignatureRequestFromFormPlacement
            {
                DocumentIndex = 0,
                Page = evt.SignatureAnchor.Page,
                X = evt.SignatureAnchor.X,
                Y = evt.SignatureAnchor.Y,
                W = evt.SignatureAnchor.W,
                H = evt.SignatureAnchor.H,
            },
            AutoSend = true,
        };
    }

    // Firmante del formulario: participante externo (con email) o cliente registrado (email/nombre vacíos;
    // Signature los resuelve por CustomerId desde su propia proyección). Null = no hay a quién pedir firma.
    private async Task<SignerIdentity?> ResolveSignerAsync(FormRequest request, int revision, CancellationToken ct)
    {
        if (request.ParticipantId is { } participantId)
        {
            var participant = await participants.GetAsync(request.TenantId, participantId, ct);
            if (participant is null || string.IsNullOrWhiteSpace(participant.Email))
            {
                logger.LogWarning(
                    "Signature skipped for request {RequestId}: participant {ParticipantId} not found or has no email.",
                    request.Id,
                    participantId
                );
                return null;
            }
            return new SignerIdentity(
                participant.Email,
                string.IsNullOrWhiteSpace(participant.FullName) ? participant.Email : participant.FullName!,
                participant.PhoneE164,
                participant.Language,
                participant.LinkedCustomerId ?? request.EffectiveCustomerId
            );
        }

        // Cliente sin participante: Tasks no guarda su email (proyección delgada). Manda el CustomerId y
        // Signature resuelve email y nombre desde su proyección de clientes.
        if (request.EffectiveCustomerId is { } customerId)
            return new SignerIdentity(string.Empty, string.Empty, Phone: null, Language: null, customerId);

        logger.LogInformation(
            "Signature skipped for request {RequestId} rev {Revision}: no participant or customer to sign.",
            request.Id,
            revision
        );
        return null;
    }

    private sealed record SignerIdentity(
        string Email,
        string FullName,
        string? Phone,
        string? Language,
        Guid? CustomerId
    );

    // Lee verification=emailOtp|smsOtp del signatureStep; default EmailOtp.
    private async Task<string> ResolveVerificationAsync(FormRequest request, CancellationToken ct)
    {
        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return "EmailOtp";
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        if (definition.IsFailure)
            return "EmailOtp";
        var step = FindSignatureStep(definition.Value);
        if (step is null)
            return "EmailOtp";
        if (
            step.Props.ValueKind == JsonValueKind.Object
            && step.Props.TryGetProperty("verification", out var value)
            && value.ValueKind == JsonValueKind.String
        )
            return string.Equals(value.GetString(), "smsOtp", StringComparison.OrdinalIgnoreCase)
                ? "SmsOtp"
                : "EmailOtp";
        return "EmailOtp";
    }

    private static FormComponent? FindSignatureStep(FormDefinition definition) =>
        definition
            .Pages.SelectMany(page => page.Sections)
            .SelectMany(section => section.Components)
            .FirstOrDefault(component => string.Equals(component.Type, "signatureStep", StringComparison.Ordinal));
}
