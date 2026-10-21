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
        if (_attachments.Any(item => item.FileId == fileId && item.Status != FormAttachmentStatus.Detached))
            return Result.Failure<FormAttachment>(FormRequestErrors.AttachmentAlreadyRegistered);
        if (_attachments.Count(item => item.Status != FormAttachmentStatus.Detached) >= maxPerSubmission
            || _attachments.Count(item => item.Status != FormAttachmentStatus.Detached && item.Address == address) >= maxForField)
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
        var attachment = _attachments.FirstOrDefault(item => item.Id == attachmentId);
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
        foreach (var current in _sensitiveAnswers.Where(item => item.Address == address && item.SupersededAtUtc is null))
            current.Supersede(nowUtc);
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
        var availableFiles = _attachments
            .Where(item => item.Status == FormAttachmentStatus.Available)
            .Select(item => item.FileId)
            .ToHashSet();
        if (referencedFileIds.Any(fileId => !availableFiles.Contains(fileId)))
            return Result.Failure<FormSubmissionRevision>(FormRequestErrors.AttachmentsNotReady);

        var activeSensitive = _sensitiveAnswers
            .Where(item => item.SupersededAtUtc is null)
            .Select(item => item.Id)
            .ToHashSet();
        if (referencedSensitiveIds.Any(answerId => !activeSensitive.Contains(answerId)))
            return Result.Failure<FormSubmissionRevision>(FormRequestErrors.SensitiveAnswerMissing);

        var revision = FormSubmissionRevision.Create(Id, revisionNumber, answers, actor, nowUtc);
        _revisions.Add(revision);
        return Result.Success(revision);
    }

    internal FormSubmissionRevision? CurrentRevision(int revisionNumber) =>
        _revisions.SingleOrDefault(item => item.RevisionNumber == revisionNumber);
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
    public IReadOnlyCollection<FormFieldComment> FieldComments => _fieldComments;

    internal static FormSubmissionRevision Create(
        Guid submissionId,
        int revisionNumber,
        ProtectedAnswers answers,
        FormActor actor,
        DateTime nowUtc
    ) => new()
    {
        FormSubmissionId = submissionId,
        RevisionNumber = revisionNumber,
        Answers = answers,
        SubmittedAtUtc = nowUtc,
        SubmittedBy = actor.Copy(),
        Outcome = FormRevisionOutcome.Pending,
    };

    internal void Return(string message, IReadOnlyCollection<FormFieldCommentInput> comments, Guid byUserId, DateTime nowUtc)
    {
        Outcome = FormRevisionOutcome.Returned;
        ReviewMessage = message;
        ReviewedAtUtc = nowUtc;
        ReviewedByUserId = byUserId;
        _fieldComments.Clear();
        _fieldComments.AddRange(comments.Select(item => FormFieldComment.Create(Id, item.Address, item.Comment)));
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
}

public sealed record FormFieldCommentInput(FormAnswerAddress Address, string Comment);

public sealed class FormFieldComment : BaseEntity
{
    private FormFieldComment() { }

    public Guid RevisionId { get; private set; }
    public string ComponentId { get; private set; } = string.Empty;
    public string? RowId { get; private set; }
    public string Comment { get; private set; } = string.Empty;

    internal static FormFieldComment Create(Guid revisionId, FormAnswerAddress address, string comment) => new()
    {
        RevisionId = revisionId,
        ComponentId = address.ComponentId,
        RowId = address.RowId,
        Comment = comment.Trim(),
    };
}
