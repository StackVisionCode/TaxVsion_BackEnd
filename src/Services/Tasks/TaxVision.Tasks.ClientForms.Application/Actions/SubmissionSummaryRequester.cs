using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// Pide a Documents el Submission Summary de una revisión completada: arma el snapshot (minimizado y
/// enmascarado), marca la revisión en PdfStatus=Requested con un GenerationId nuevo y deja el comando
/// cifrado en `outgoing`. No guarda ni publica: quien lo invoca guarda y después publica (T-8).
/// </summary>
public sealed class SubmissionSummaryRequester(
    IFormTemplateVersionRepository versions,
    IFormEngine engine,
    IAnswersProtector protector,
    IProtectedPayloadCodec codec,
    IFormRequestStaffReadRepository staffRead,
    ICorrelationContext correlation
)
{
    private const string SignatureOwnerType = "Signature";
    private const string SignatureFolderType = "Signatures";

    public async Task<Result> RequestAsync(
        FormRequest request,
        int revisionNumber,
        DeferredMessages outgoing,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var revision = request.Submission?.CurrentRevision(revisionNumber);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);

        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure(version.Error);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        if (definition.IsFailure)
            return Result.Failure(definition.Error);
        var answers = engine.ParseAnswers(protector.Unprotect(revision.Answers));
        if (answers.IsFailure)
            return Result.Failure(answers.Error);

        var visibility = engine.EvaluateVisibility(definition.Value, answers.Value);
        var masks = BuildMasks(request.Submission!);
        var attachments = BuildAttachments(request.Submission!);
        var label = await staffRead.GetParticipantLabelAsync(request.TenantId, request, ct);

        var header = new SubmissionSummarySnapshotBuilder.Header(
            request.Title,
            label.DisplayName,
            request.VersionNumber,
            revisionNumber,
            revision.SubmittedAtUtc,
            request.CompletedAtUtc ?? nowUtc
        );
        var snapshot = SubmissionSummarySnapshotBuilder.Build(
            definition.Value,
            answers.Value,
            visibility,
            masks,
            attachments,
            header
        );

        var generationId = Guid.NewGuid();
        var marked = request.MarkPdfRequested(revisionNumber, generationId);
        if (marked.IsFailure)
            return marked;

        // P-18: si la plantilla tiene un signatureStep, Documents genera también la copia para firma
        // (owner/carpeta de firma) y devuelve Copies + ancla.
        var signatureCopy = ResolveSignatureCopy(request, revisionNumber, definition.Value);

        var message = SubmissionSummaryCommandFactory.Create(
            request,
            revisionNumber,
            generationId,
            snapshot,
            codec,
            correlation.CorrelationId,
            signatureCopy
        );
        outgoing.Add(message);
        return Result.Success();
    }

    // Todo activado (decisión del dueño): la firma la activa el signatureStep de la plantilla (decisión del
    // preparador) + el plan planner. Sin sub-flag por oficina que alguien deba recordar encender.
    // Un reintento del PDF no pide una segunda firma si la revisión ya tiene una en curso o terminada.
    private static SignatureCopyRequest? ResolveSignatureCopy(
        FormRequest request,
        int revisionNumber,
        FormDefinition definition
    )
    {
        if (!DefinitionHasSignatureStep(definition) || !request.RevisionAwaitsSignature(revisionNumber))
            return null;
        return new SignatureCopyRequest
        {
            OwnerType = SignatureOwnerType,
            FolderType = SignatureFolderType,
            // FolderType.Signatures exige año; si la request no lo tiene, cae al año en curso.
            TaxYear = request.TaxYear ?? DateTime.UtcNow.Year,
        };
    }

    private static bool DefinitionHasSignatureStep(FormDefinition definition) =>
        definition.Pages.Any(page =>
            page.Sections.Any(section =>
                section.Components.Any(component =>
                    string.Equals(component.Type, "signatureStep", StringComparison.Ordinal)
                )
            )
        );

    private static IReadOnlyCollection<SubmissionSummarySnapshotBuilder.SensitiveMask> BuildMasks(
        FormSubmission submission
    )
    {
        var masks = new List<SubmissionSummarySnapshotBuilder.SensitiveMask>();
        foreach (var answer in submission.SensitiveAnswers)
        {
            if (answer.SupersededAtUtc is null)
                masks.Add(
                    new SubmissionSummarySnapshotBuilder.SensitiveMask(
                        answer.ComponentId,
                        answer.RowId,
                        answer.Protected.Last4
                    )
                );
        }
        return masks;
    }

    private static IReadOnlyCollection<SubmissionSummarySnapshotBuilder.AttachmentRef> BuildAttachments(
        FormSubmission submission
    )
    {
        var attachments = new List<SubmissionSummarySnapshotBuilder.AttachmentRef>();
        foreach (var attachment in submission.Attachments)
        {
            if (attachment.Status == FormAttachmentStatus.Available)
                attachments.Add(
                    new SubmissionSummarySnapshotBuilder.AttachmentRef(
                        attachment.ComponentId,
                        attachment.RowId,
                        attachment.DisplayName
                    )
                );
        }
        return attachments;
    }
}
