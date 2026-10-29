using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed class FormSubmission : BaseEntity
{
    private readonly List<FormSubmissionRevision> _revisions = [];
    private readonly List<FormAttachment> _attachments = [];
    private readonly List<FormSensitiveAnswer> _sensitiveAnswers = [];

    private FormSubmission() { }

    public Guid FormRequestId { get; private set; }
    public ProtectedAnswers? DraftAnswers { get; private set; }
    public DateTime? DraftUpdatedAtUtc { get; private set; }
    public FormActor? DraftUpdatedBy { get; private set; }
    public IReadOnlyCollection<FormSubmissionRevision> Revisions => _revisions;
    public IReadOnlyCollection<FormAttachment> Attachments => _attachments;
    public IReadOnlyCollection<FormSensitiveAnswer> SensitiveAnswers => _sensitiveAnswers;

    internal static FormSubmission Create(Guid formRequestId, ProtectedAnswers? draft, DateTime nowUtc)
    {
        var submission = new FormSubmission { FormRequestId = formRequestId };
        if (draft is not null)
        {
            submission.DraftAnswers = draft;
            submission.DraftUpdatedAtUtc = nowUtc;
        }
        return submission;
    }

    internal void SaveDraft(ProtectedAnswers answers, FormActor actor, DateTime nowUtc)
    {
        DraftAnswers = answers;
        DraftUpdatedAtUtc = nowUtc;
        DraftUpdatedBy = actor.Copy();
    }

    // Re-cifra el borrador con la clave actual (X-9). No toca la auditoría del último editor.
    public void ReEncryptDraft(ProtectedAnswers reEncrypted)
    {
        ArgumentNullException.ThrowIfNull(reEncrypted);
        if (DraftAnswers is not null)
            DraftAnswers = reEncrypted;
    }

    internal Result<FormAttachment> RegisterAttachment(
        Guid tenantId,
        FormAnswerAddress address,
        Guid fileId,
        string displayName,
        string contentType,
        long sizeBytes,
        int maxForField,
        int maxPerSubmission,
        FormFileVerdict? knownVerdict,
        IReadOnlyCollection<(string OwnerType, Guid OwnerId)> expectedOwners,
        DateTime nowUtc
    )
    {
        var activeCount = 0;
        var fieldCount = 0;
        foreach (var current in _attachments)
        {
            if (current.Status == FormAttachmentStatus.Detached)
                continue;
            if (current.FileId == fileId)
                return Result.Failure<FormAttachment>(FormRequestErrors.AttachmentAlreadyRegistered);
            activeCount++;
            if (current.Address == address)
                fieldCount++;
        }
        if (activeCount >= maxPerSubmission || fieldCount >= maxForField)
            return Result.Failure<FormAttachment>(FormRequestErrors.TooManyAttachments);

        var attachment = FormAttachment.Create(
            tenantId,
            Id,
            address,
            fileId,
            displayName,
            contentType,
            sizeBytes,
            nowUtc
        );
        _attachments.Add(attachment);
        if (knownVerdict is not null)
            attachment.ApplyVerdict(knownVerdict, expectedOwners, nowUtc);
        return Result.Success(attachment);
    }

    internal Result DetachAttachment(Guid attachmentId, DateTime nowUtc)
    {
        FormAttachment? attachment = null;
        foreach (var current in _attachments)
        {
            if (current.Id == attachmentId)
            {
                attachment = current;
                break;
            }
        }
        if (attachment is null)
            return Result.Failure(FormRequestErrors.AttachmentNotFound);
        attachment.Detach(nowUtc);
        return Result.Success();
    }

    internal FormSensitiveAnswer CaptureSensitiveAnswer(
        FormAnswerAddress address,
        FormTaxIdentifierKind kind,
        ProtectedTaxIdentifier value,
        FormActor actor,
        DateTime nowUtc
    )
    {
        foreach (var current in _sensitiveAnswers)
        {
            if (current.Address == address && current.SupersededAtUtc is null)
                current.Supersede(nowUtc);
        }
        var answer = FormSensitiveAnswer.Create(Id, address, kind, value, actor, nowUtc);
        _sensitiveAnswers.Add(answer);
        return answer;
    }

    internal Result<FormSubmissionRevision> Submit(
        int revisionNumber,
        ProtectedAnswers answers,
        IReadOnlyCollection<Guid> referencedFileIds,
        IReadOnlyCollection<Guid> referencedSensitiveIds,
        FormActor actor,
        DateTime nowUtc
    )
    {
        foreach (var fileId in referencedFileIds)
        {
            if (!HasAvailableFile(fileId))
                return Result.Failure<FormSubmissionRevision>(FormRequestErrors.AttachmentsNotReady);
        }

        foreach (var answerId in referencedSensitiveIds)
        {
            if (!HasActiveSensitiveAnswer(answerId))
                return Result.Failure<FormSubmissionRevision>(FormRequestErrors.SensitiveAnswerMissing);
        }

        var revision = FormSubmissionRevision.Create(Id, revisionNumber, answers, actor, nowUtc);
        _revisions.Add(revision);
        return Result.Success(revision);
    }

    // X-5 — purga de retención: borra el borrador cifrado. Los sensibles los quita el job por fila.
    public void PurgeDraftForRetention()
    {
        DraftAnswers = null;
        DraftUpdatedAtUtc = null;
        DraftUpdatedBy = null;
    }

    // X-5 — desvincula los adjuntos no desvinculados y devuelve sus FileIds para pedir el borrado.
    public IReadOnlyList<Guid> DetachAttachmentsForRetention(DateTime nowUtc)
    {
        var fileIds = new List<Guid>();
        foreach (var attachment in _attachments)
        {
            if (attachment.Status == FormAttachmentStatus.Detached)
                continue;
            fileIds.Add(attachment.FileId);
            attachment.Detach(nowUtc);
        }
        return fileIds;
    }

    internal FormSubmissionRevision? CurrentRevision(int revisionNumber) =>
        _revisions.Find(item => item.RevisionNumber == revisionNumber);

    internal FormSubmissionRevision? RevisionForPdf(Guid generationId) =>
        _revisions.Find(item => item.PdfGenerationId == generationId);

    private bool HasAvailableFile(Guid fileId)
    {
        foreach (var attachment in _attachments)
        {
            if (attachment.FileId == fileId && attachment.Status == FormAttachmentStatus.Available)
                return true;
        }
        return false;
    }

    private bool HasActiveSensitiveAnswer(Guid answerId)
    {
        foreach (var answer in _sensitiveAnswers)
        {
            if (answer.Id == answerId && answer.SupersededAtUtc is null)
                return true;
        }
        return false;
    }
}

public sealed class FormSubmissionRevision : BaseEntity
{
    private readonly List<FormFieldComment> _fieldComments = [];

    private FormSubmissionRevision() { }

    public Guid FormSubmissionId { get; private set; }
    public int RevisionNumber { get; private set; }
    public ProtectedAnswers Answers { get; private set; } = default!;
    public DateTime SubmittedAtUtc { get; private set; }
    public FormActor SubmittedBy { get; private set; } = default!;
    public FormRevisionOutcome Outcome { get; private set; }
    public string? ReviewMessage { get; private set; }
    public DateTime? ReviewedAtUtc { get; private set; }
    public Guid? ReviewedByUserId { get; private set; }
    public string? CompletionNote { get; private set; }
    public FormRevisionPdfStatus PdfStatus { get; private set; }
    public Guid? PdfGenerationId { get; private set; }
    public Guid? PdfFileId { get; private set; }

    // P-18 — firma del Submission Summary de esta revisión (inmutable: el hash sale de AnswersCipher).
    public FormRevisionSignatureStatus SignatureStatus { get; private set; }
    public Guid? SignatureRequestId { get; private set; }
    public Guid? SignatureCopyFileId { get; private set; }
    public string? DocumentHashPre { get; private set; }
    public string? DocumentHashPost { get; private set; }
    public Guid? SealedFileId { get; private set; }
    public Guid? SignatureCertificateFileId { get; private set; }

    public bool HasPendingSignature =>
        SignatureStatus
            is FormRevisionSignatureStatus.Requesting
                or FormRevisionSignatureStatus.Sent
                or FormRevisionSignatureStatus.Signed
                or FormRevisionSignatureStatus.Completed;

    // Sin firma todavía, o con una que falló por causa técnica: un PDF nuevo puede volver a pedirla.
    public bool AwaitsSignature =>
        SignatureStatus is FormRevisionSignatureStatus.None or FormRevisionSignatureStatus.Failed;

    private bool IsSignatureTerminal =>
        SignatureStatus
            is FormRevisionSignatureStatus.Sealed
                or FormRevisionSignatureStatus.Declined
                or FormRevisionSignatureStatus.Canceled
                or FormRevisionSignatureStatus.Expired;

    public IReadOnlyCollection<FormFieldComment> FieldComments => _fieldComments;

    internal static FormSubmissionRevision Create(
        Guid submissionId,
        int revisionNumber,
        ProtectedAnswers answers,
        FormActor actor,
        DateTime nowUtc
    ) =>
        new()
        {
            FormSubmissionId = submissionId,
            RevisionNumber = revisionNumber,
            Answers = answers,
            SubmittedAtUtc = nowUtc,
            SubmittedBy = actor.Copy(),
            Outcome = FormRevisionOutcome.Pending,
        };

    // Re-cifra las respuestas de la revisión con la clave actual (X-9). La firma se valida contra el
    // hash del PDF en CloudStorage, no contra este ciphertext, así que re-cifrar no la invalida.
    public void ReEncryptAnswers(ProtectedAnswers reEncrypted)
    {
        ArgumentNullException.ThrowIfNull(reEncrypted);
        Answers = reEncrypted;
    }

    internal void Return(
        string message,
        IReadOnlyCollection<FormFieldCommentInput> comments,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        Outcome = FormRevisionOutcome.Returned;
        ReviewMessage = message;
        ReviewedAtUtc = nowUtc;
        ReviewedByUserId = byUserId;
        _fieldComments.Clear();
        foreach (var item in comments)
            _fieldComments.Add(FormFieldComment.Create(Id, item.Address, item.Comment));
    }

    internal void Complete(string? note, Guid byUserId, DateTime nowUtc)
    {
        Outcome = FormRevisionOutcome.Completed;
        CompletionNote = note;
        ReviewedAtUtc = nowUtc;
        ReviewedByUserId = byUserId;
    }

    internal void MarkPdfRequested(Guid generationId)
    {
        PdfStatus = FormRevisionPdfStatus.Requested;
        PdfGenerationId = generationId;
        PdfFileId = null;
    }

    internal void LinkPdf(Guid fileId)
    {
        PdfStatus = FormRevisionPdfStatus.Ready;
        PdfFileId = fileId;
    }

    internal void MarkPdfFailed() => PdfStatus = FormRevisionPdfStatus.Failed;

    // --- Firma (P-18). Cada transición es idempotente y respeta los estados terminales. ---

    internal void MarkSignatureRequesting(Guid copyFileId, string documentHashPre)
    {
        SignatureStatus = FormRevisionSignatureStatus.Requesting;
        SignatureCopyFileId = copyFileId;
        DocumentHashPre = documentHashPre;
        SignatureRequestId = null;
    }

    internal void LinkSignatureRequest(Guid signatureRequestId)
    {
        if (SignatureStatus is FormRevisionSignatureStatus.Requesting or FormRevisionSignatureStatus.Sent)
        {
            SignatureRequestId = signatureRequestId;
            SignatureStatus = FormRevisionSignatureStatus.Sent;
        }
    }

    internal void MarkSignatureFailed()
    {
        if (SignatureStatus is FormRevisionSignatureStatus.Requesting or FormRevisionSignatureStatus.Sent)
            SignatureStatus = FormRevisionSignatureStatus.Failed;
    }

    internal void MarkSignatureSigned()
    {
        if (SignatureStatus is FormRevisionSignatureStatus.Sent or FormRevisionSignatureStatus.Signed)
            SignatureStatus = FormRevisionSignatureStatus.Signed;
    }

    internal void MarkSignatureCompleted()
    {
        if (
            SignatureStatus
            is FormRevisionSignatureStatus.Sent
                or FormRevisionSignatureStatus.Signed
                or FormRevisionSignatureStatus.Completed
        )
            SignatureStatus = FormRevisionSignatureStatus.Completed;
    }

    internal void MarkSignatureSealed(Guid sealedFileId, string documentHashPost, Guid? certificateFileId)
    {
        if (
            SignatureStatus
            is FormRevisionSignatureStatus.Sent
                or FormRevisionSignatureStatus.Signed
                or FormRevisionSignatureStatus.Completed
                or FormRevisionSignatureStatus.Sealed
        )
        {
            SealedFileId = sealedFileId;
            DocumentHashPost = documentHashPost;
            SignatureCertificateFileId = certificateFileId;
            SignatureStatus = FormRevisionSignatureStatus.Sealed;
        }
    }

    internal void MarkSignatureDeclined()
    {
        if (!IsSignatureTerminal)
            SignatureStatus = FormRevisionSignatureStatus.Declined;
    }

    internal void MarkSignatureCanceled()
    {
        if (!IsSignatureTerminal)
            SignatureStatus = FormRevisionSignatureStatus.Canceled;
    }

    internal void MarkSignatureExpired()
    {
        if (!IsSignatureTerminal)
            SignatureStatus = FormRevisionSignatureStatus.Expired;
    }
}

public sealed record FormFieldCommentInput(FormAnswerAddress Address, string Comment);

public sealed class FormFieldComment : BaseEntity
{
    private FormFieldComment() { }

    public Guid RevisionId { get; private set; }
    public string ComponentId { get; private set; } = string.Empty;
    public string? RowId { get; private set; }
    public string Comment { get; private set; } = string.Empty;

    internal static FormFieldComment Create(Guid revisionId, FormAnswerAddress address, string comment) =>
        new()
        {
            RevisionId = revisionId,
            ComponentId = address.ComponentId,
            RowId = address.RowId,
            Comment = comment.Trim(),
        };
}
