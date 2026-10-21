using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Requests.Events;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

public sealed class FormRequest : AggregateRoot
{
    private FormRequest() { }

    public FormParticipantKind ParticipantKind { get; private set; }
    public Guid? CustomerId { get; private set; }
    public Guid? ParticipantId { get; private set; }
    public CustomerLinkStatus CustomerLinkStatus { get; private set; }
    public Guid? LinkBlockedCustomerId { get; private set; }
    public string? LinkBlockedReason { get; private set; }
    public Guid? LinkConflictingRequestId { get; private set; }
    public FormRequestOrigin Origin { get; private set; }
    public Guid? IntakeLinkId { get; private set; }
    public Guid? ResponsibleUserId { get; private set; }
    public Guid FormTemplateId { get; private set; }
    public Guid FormTemplateVersionId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Title { get; private set; } = string.Empty;
    public string? MessageToClient { get; private set; }
    public int? TaxYear { get; private set; }
    public Guid? TaskId { get; private set; }
    public DateTime? DueAtUtc { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public DateTime RequestedAtUtc { get; private set; }
    public FormRequestStatus Status { get; private set; }
    public DateTime? FirstOpenedAtUtc { get; private set; }
    public DateTime? LastSubmittedAtUtc { get; private set; }
    public int CurrentRevisionNumber { get; private set; }
    public int DraftProgressPercent { get; private set; }
    public DateTime? CompletedAtUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public string? CompletionNote { get; private set; }
    public DateTime? CancelledAtUtc { get; private set; }
    public Guid? CancelledByUserId { get; private set; }
    public FormCancelReason? CancelReason { get; private set; }
    public string? CancelNote { get; private set; }
    public int ReminderCount { get; private set; }
    public DateTime? LastReminderAtUtc { get; private set; }
    public Guid? PrefillSourceRequestId { get; private set; }
    public FormProfileUpdateStatus ProfileUpdateStatus { get; private set; }
    public DateTime LastActivityAtUtc { get; private set; }
    public FormSubmission? Submission { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public FormParticipantRef Participant => ParticipantKind == FormParticipantKind.Customer
        ? FormParticipantRef.Customer(CustomerId ?? Guid.Empty)
        : FormParticipantRef.External(ParticipantId ?? Guid.Empty);

    public CustomerLinkState CustomerLink => CustomerLinkStatus switch
    {
        CustomerLinkStatus.Linked => CustomerLinkState.Linked(CustomerId ?? Guid.Empty),
        CustomerLinkStatus.Blocked => CustomerLinkState.Blocked(
            LinkBlockedCustomerId ?? Guid.Empty,
            LinkBlockedReason ?? string.Empty,
            LinkConflictingRequestId
        ),
        _ => CustomerLinkState.None(),
    };

    public Guid? EffectiveCustomerId => CustomerId;

    public static Result<FormRequest> Create(
        Guid tenantId,
        FormParticipantRef participant,
        FormTemplateVersion version,
        string? title,
        string? messageToClient,
        int? taxYear,
        Guid? taskId,
        DateTime? dueAtUtc,
        Guid? prefillSourceRequestId,
        ProtectedAnswers? prefilledDraft,
        FormRequestOrigin origin,
        Guid? intakeLinkId,
        Guid? responsibleUserId,
        Guid requestedByUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty || requestedByUserId == Guid.Empty || !participant.IsValid)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidParticipant);
        if (version.TenantId != tenantId)
            return Result.Failure<FormRequest>(FormRequestErrors.VersionMismatch);
        if (participant.Kind == FormParticipantKind.External
            && (!responsibleUserId.HasValue || responsibleUserId.Value == Guid.Empty))
            return Result.Failure<FormRequest>(FormRequestErrors.ResponsibleUserRequired);
        if (participant.Kind == FormParticipantKind.External && (taskId.HasValue || prefillSourceRequestId.HasValue))
            return Result.Failure<FormRequest>(FormRequestErrors.TaskRequiresCustomer);
        if (origin == FormRequestOrigin.IntakeLink
            && (participant.Kind != FormParticipantKind.External
                || !intakeLinkId.HasValue
                || intakeLinkId.Value == Guid.Empty))
            return Result.Failure<FormRequest>(FormRequestErrors.IntakeLinkRequired);
        if (origin == FormRequestOrigin.Directed && intakeLinkId.HasValue)
            return Result.Failure<FormRequest>(FormRequestErrors.IntakeLinkRequired);

        var normalizedTitle = string.IsNullOrWhiteSpace(title) ? version.Title.Trim() : title.Trim();
        if (normalizedTitle.Length is < 1 or > 200)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidTitle);
        var normalizedMessage = string.IsNullOrWhiteSpace(messageToClient) ? null : messageToClient.Trim();
        if (normalizedMessage?.Length > 2000)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidMessage);
        if (taxYear is < 2000 or > 2100)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidTaxYear);
        if (dueAtUtc.HasValue && dueAtUtc.Value <= nowUtc)
            return Result.Failure<FormRequest>(FormRequestErrors.InvalidDueDate);

        var request = new FormRequest
        {
            ParticipantKind = participant.Kind,
            CustomerId = participant.CustomerId,
            ParticipantId = participant.ParticipantId,
            CustomerLinkStatus = CustomerLinkStatus.None,
            Origin = origin,
            IntakeLinkId = intakeLinkId,
            ResponsibleUserId = responsibleUserId,
            FormTemplateId = version.FormTemplateId,
            FormTemplateVersionId = version.Id,
            VersionNumber = version.VersionNumber,
            Title = normalizedTitle,
            MessageToClient = normalizedMessage,
            TaxYear = taxYear,
            TaskId = taskId,
            DueAtUtc = dueAtUtc,
            RequestedByUserId = requestedByUserId,
            RequestedAtUtc = nowUtc,
            Status = FormRequestStatus.Pending,
            PrefillSourceRequestId = prefillSourceRequestId,
            LastActivityAtUtc = nowUtc,
        };
        request.SetTenant(tenantId);
        if (prefilledDraft is not null)
            request.Submission = FormSubmission.Create(request.Id, prefilledDraft, nowUtc);
        request.AddDomainEvent(new FormRequestAssigned(
            request.Id,
            tenantId,
            participant.Kind,
            participant.CustomerId,
            participant.ParticipantId,
            requestedByUserId,
            nowUtc
        ));
        return Result.Success(request);
    }

    public bool MarkOpened(DateTime nowUtc)
    {
        if (FirstOpenedAtUtc.HasValue)
            return false;
        FirstOpenedAtUtc = nowUtc;
        Touch(nowUtc);
        return true;
    }

    public Result SaveDraft(ProtectedAnswers answers, int progressPercent, FormActor actor, DateTime nowUtc)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;
        var actorCheck = EnsureActor(actor);
        if (actorCheck.IsFailure)
            return actorCheck;
        if (progressPercent is < 0 or > 100)
            return Result.Failure(FormRequestErrors.InvalidComponent);

        Submission ??= FormSubmission.Create(Id, null, nowUtc);
        Submission.SaveDraft(answers, actor, nowUtc);
        DraftProgressPercent = progressPercent;
        Touch(nowUtc);
        return Result.Success();
    }

    public Result<FormAttachment> RegisterAttachment(
        FormAnswerAddress address,
        Guid fileId,
        string displayName,
        string contentType,
        long sizeBytes,
        int maxForField,
        int maxPerSubmission,
        FormFileVerdict? knownVerdict,
        DateTime nowUtc
    )
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result.Failure<FormAttachment>(editable.Error);
        if (fileId == Guid.Empty || sizeBytes < 0 || string.IsNullOrWhiteSpace(displayName) || displayName.Length > 255
            || string.IsNullOrWhiteSpace(contentType) || contentType.Length > 128 || maxForField < 1 || maxPerSubmission < 1)
            return Result.Failure<FormAttachment>(FormRequestErrors.InvalidComponent);

        Submission ??= FormSubmission.Create(Id, null, nowUtc);
        var result = Submission.RegisterAttachment(
            TenantId,
            address,
            fileId,
            displayName,
            contentType,
            sizeBytes,
            maxForField,
            maxPerSubmission,
            knownVerdict,
            ExpectedFileOwners(),
            nowUtc
        );
        if (result.IsSuccess)
            Touch(nowUtc);
        return result;
    }

    public Result DetachAttachment(Guid attachmentId, DateTime nowUtc)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;
        if (Submission is null)
            return Result.Failure(FormRequestErrors.AttachmentNotFound);
        var result = Submission.DetachAttachment(attachmentId, nowUtc);
        if (result.IsSuccess)
            Touch(nowUtc);
        return result;
    }

    public FileVerdictOutcome ApplyFileVerdict(Guid fileId, FormFileVerdict verdict, DateTime nowUtc)
    {
        if (Submission is null)
            return FileVerdictOutcome.Ignored;

        var overall = FileVerdictOutcome.Ignored;
        foreach (var attachment in Submission.Attachments.Where(item => item.FileId == fileId))
        {
            var previousStatus = attachment.Status;
            var outcome = attachment.ApplyVerdict(verdict, ExpectedFileOwners(), nowUtc);
            if (outcome != FileVerdictOutcome.Ignored)
                overall = outcome;
            if (outcome == FileVerdictOutcome.Rejected && previousStatus != FormAttachmentStatus.Rejected)
                AddDomainEvent(new FormAttachmentRejected(Id, TenantId, attachment.Id, fileId, attachment.RejectionCode!, nowUtc));
        }
        if (overall != FileVerdictOutcome.Ignored)
            Touch(nowUtc);
        return overall;
    }

    public int ExpireStalePendingAttachments(DateTime olderThanUtc, DateTime nowUtc)
    {
        if (Submission is null)
            return 0;
        var stale = Submission.Attachments
            .Where(item => item.Status == FormAttachmentStatus.Pending && item.RegisteredAtUtc < olderThanUtc)
            .ToList();
        foreach (var attachment in stale)
            attachment.Expire(nowUtc);
        if (stale.Count > 0)
            Touch(nowUtc);
        return stale.Count;
    }

    public Result<FormSensitiveAnswer> CaptureSensitiveAnswer(
        FormAnswerAddress address,
        FormTaxIdentifierKind kind,
        ProtectedTaxIdentifier value,
        FormActor actor,
        DateTime nowUtc
    )
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result.Failure<FormSensitiveAnswer>(editable.Error);
        var actorCheck = EnsureActor(actor);
        if (actorCheck.IsFailure)
            return Result.Failure<FormSensitiveAnswer>(actorCheck.Error);
        Submission ??= FormSubmission.Create(Id, null, nowUtc);
        var answer = Submission.CaptureSensitiveAnswer(address, kind, value, actor, nowUtc);
        Touch(nowUtc);
        return Result.Success(answer);
    }

    public Result<FormSubmissionRevision> Submit(
        ProtectedAnswers sanitizedAnswers,
        IReadOnlyCollection<Guid> referencedFileIds,
        IReadOnlyCollection<Guid> referencedSensitiveIds,
        FormActor actor,
        DateTime nowUtc
    )
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return Result.Failure<FormSubmissionRevision>(editable.Error);
        var actorCheck = EnsureActor(actor);
        if (actorCheck.IsFailure)
            return Result.Failure<FormSubmissionRevision>(actorCheck.Error);
        Submission ??= FormSubmission.Create(Id, null, nowUtc);
        var revisionNumber = CurrentRevisionNumber + 1;
        var result = Submission.Submit(
            revisionNumber,
            sanitizedAnswers,
            referencedFileIds,
            referencedSensitiveIds,
            actor,
            nowUtc
        );
        if (result.IsFailure)
            return result;

        CurrentRevisionNumber = revisionNumber;
        Status = FormRequestStatus.Submitted;
        LastSubmittedAtUtc = nowUtc;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestSubmitted(
            Id,
            TenantId,
            revisionNumber,
            actor.Kind,
            actor.PortalUserId,
            actor.ParticipantId,
            revisionNumber > 1,
            nowUtc
        ));
        return result;
    }

    public Result ReturnForChanges(
        string? message,
        IReadOnlyCollection<FormFieldCommentInput> comments,
        Guid byUserId,
        DateTime nowUtc
    )
    {
        var revision = GetReviewableRevision();
        if (revision.IsFailure)
            return Result.Failure(revision.Error);
        var normalized = message?.Trim();
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Length > 2000 || comments.Count > 100
            || comments.Any(item => string.IsNullOrWhiteSpace(item.Comment) || item.Comment.Trim().Length > 1000))
            return Result.Failure(FormRequestErrors.InvalidMessage);
        revision.Value.Return(normalized, comments, byUserId, nowUtc);
        Status = FormRequestStatus.Returned;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestReturned(Id, TenantId, CurrentRevisionNumber, byUserId, nowUtc));
        return Result.Success();
    }

    public Result Complete(string? note, Guid byUserId, DateTime nowUtc)
    {
        var revision = GetReviewableRevision();
        if (revision.IsFailure)
            return Result.Failure(revision.Error);
        var normalized = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalized?.Length > 2000)
            return Result.Failure(FormRequestErrors.InvalidMessage);
        revision.Value.Complete(normalized, byUserId, nowUtc);
        Status = FormRequestStatus.Completed;
        CompletedAtUtc = nowUtc;
        CompletedByUserId = byUserId;
        CompletionNote = normalized;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestCompleted(Id, TenantId, CurrentRevisionNumber, byUserId, nowUtc));
        return Result.Success();
    }

    public Result Cancel(string? note, Guid byUserId, DateTime nowUtc)
    {
        if (Status is FormRequestStatus.Completed or FormRequestStatus.Cancelled)
            return Result.Failure(FormRequestErrors.InvalidTransition);
        var normalized = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        if (normalized?.Length > 2000)
            return Result.Failure(FormRequestErrors.InvalidMessage);
        Status = FormRequestStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelledByUserId = byUserId;
        CancelReason = FormCancelReason.StaffCancelled;
        CancelNote = normalized;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestCancelled(Id, TenantId, FormCancelReason.StaffCancelled, FirstOpenedAtUtc.HasValue, nowUtc));
        return Result.Success();
    }

    public Result CancelBecauseCustomerArchived(Guid customerId, DateTime nowUtc)
    {
        if (EffectiveCustomerId != customerId || Status is FormRequestStatus.Completed or FormRequestStatus.Cancelled)
            return Result.Failure(FormRequestErrors.InvalidTransition);
        Status = FormRequestStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelledByUserId = null;
        CancelReason = FormCancelReason.CustomerArchived;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestCancelled(Id, TenantId, FormCancelReason.CustomerArchived, false, nowUtc));
        return Result.Success();
    }

    public Result ChangeDueDate(DateTime? dueAtUtc, Guid byUserId, DateTime nowUtc)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;
        if (dueAtUtc.HasValue && dueAtUtc.Value <= nowUtc)
            return Result.Failure(FormRequestErrors.InvalidDueDate);
        DueAtUtc = dueAtUtc;
        Touch(nowUtc);
        AddDomainEvent(new FormRequestDueDateChanged(Id, TenantId, dueAtUtc, byUserId, nowUtc));
        return Result.Success();
    }

    public Result RecordReminder(int maxReminders, DateTime nowUtc)
    {
        var editable = EnsureEditable();
        if (editable.IsFailure)
            return editable;
        if (maxReminders < 1 || ReminderCount >= maxReminders)
            return Result.Failure(FormRequestErrors.ReminderLimitReached);
        ReminderCount++;
        LastReminderAtUtc = nowUtc;
        Touch(nowUtc);
        return Result.Success();
    }

    public Result<CustomerLinkResult> TryAttachCustomer(
        Guid customerId,
        bool customerHasOpenRequestForTemplate,
        Guid? conflictingRequestId,
        DateTime nowUtc
    )
    {
        if (ParticipantKind != FormParticipantKind.External || customerId == Guid.Empty)
            return Result.Failure<CustomerLinkResult>(FormRequestErrors.InvalidParticipant);
        if (CustomerLinkStatus == CustomerLinkStatus.Linked)
            return CustomerId == customerId
                ? Result.Success(CustomerLinkResult.Unchanged)
                : Result.Failure<CustomerLinkResult>(FormRequestErrors.AlreadyLinked);

        if (customerHasOpenRequestForTemplate)
        {
            CustomerId = null;
            CustomerLinkStatus = CustomerLinkStatus.Blocked;
            LinkBlockedCustomerId = customerId;
            LinkBlockedReason = "customer_has_open_request";
            LinkConflictingRequestId = conflictingRequestId;
            Touch(nowUtc);
            return Result.Success(CustomerLinkResult.Blocked);
        }

        CustomerId = customerId;
        CustomerLinkStatus = CustomerLinkStatus.Linked;
        LinkBlockedCustomerId = null;
        LinkBlockedReason = null;
        LinkConflictingRequestId = null;
        Touch(nowUtc);
        return Result.Success(CustomerLinkResult.Linked);
    }

    public Result<CustomerLinkResult> RetryCustomerLink(
        bool customerHasOpenRequestForTemplate,
        Guid? conflictingRequestId,
        DateTime nowUtc
    )
    {
        if (CustomerLinkStatus != CustomerLinkStatus.Blocked || LinkBlockedCustomerId is null)
            return Result.Failure<CustomerLinkResult>(FormRequestErrors.LinkNotBlocked);
        return TryAttachCustomer(LinkBlockedCustomerId.Value, customerHasOpenRequestForTemplate, conflictingRequestId, nowUtc);
    }

    public Result MarkPdfRequested(int revisionNumber, Guid generationId)
    {
        var revision = Submission?.CurrentRevision(revisionNumber);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.MarkPdfRequested(generationId);
        return Result.Success();
    }

    public Result LinkPdf(Guid generationId, Guid fileId)
    {
        var revision = Submission?.Revisions.SingleOrDefault(item => item.PdfGenerationId == generationId);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.LinkPdf(fileId);
        return Result.Success();
    }

    public Result MarkPdfFailed(Guid generationId)
    {
        var revision = Submission?.Revisions.SingleOrDefault(item => item.PdfGenerationId == generationId);
        if (revision is null)
            return Result.Failure(FormRequestErrors.NotFound);
        revision.MarkPdfFailed();
        return Result.Success();
    }

    public IReadOnlyCollection<(string OwnerType, Guid OwnerId)> ExpectedFileOwners()
    {
        if (ParticipantKind == FormParticipantKind.Customer)
            return [("Customer", CustomerId!.Value)];
        if (CustomerLinkStatus == CustomerLinkStatus.Linked)
            return [("FormParticipant", ParticipantId!.Value), ("Customer", CustomerId!.Value)];
        return [("FormParticipant", ParticipantId!.Value)];
    }

    private Result EnsureEditable() => Status is FormRequestStatus.Pending or FormRequestStatus.Returned
        ? Result.Success()
        : Result.Failure(FormRequestErrors.NotEditable);

    private Result EnsureActor(FormActor actor)
    {
        if (!actor.IsValid)
            return Result.Failure(FormRequestErrors.ActorMismatch);
        var matches = actor.Kind switch
        {
            FormActorKind.PortalUser => ParticipantKind == FormParticipantKind.Customer
                || CustomerLinkStatus == CustomerLinkStatus.Linked,
            FormActorKind.ExternalSession => ParticipantKind == FormParticipantKind.External
                && actor.ParticipantId == ParticipantId,
            _ => false,
        };
        return matches ? Result.Success() : Result.Failure(FormRequestErrors.ActorMismatch);
    }

    private Result<FormSubmissionRevision> GetReviewableRevision()
    {
        if (Status != FormRequestStatus.Submitted || Submission is null)
            return Result.Failure<FormSubmissionRevision>(FormRequestErrors.InvalidTransition);
        var revision = Submission.CurrentRevision(CurrentRevisionNumber);
        return revision is null
            ? Result.Failure<FormSubmissionRevision>(FormRequestErrors.InvalidTransition)
            : Result.Success(revision);
    }

    private void Touch(DateTime nowUtc) => LastActivityAtUtc = nowUtc;
}
