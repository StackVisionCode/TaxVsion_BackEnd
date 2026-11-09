using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Domain.Requests.Events;

namespace TaxVision.Tasks.ClientForms.Domain.Requests;

/// <summary>Los adjuntos del formulario: alta, veredicto del escaneo y traspaso al cliente. Parte del agregado <see cref="FormRequest"/>.</summary>
public sealed class FormRequestFiles
{
    private readonly FormRequest _request;

    internal FormRequestFiles(FormRequest request) => _request = request;

    public Result<FormAttachment> Register(
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
        var editable = _request.EnsureEditable();
        if (editable.IsFailure)
            return Result.Failure<FormAttachment>(editable.Error);
        if (
            fileId == Guid.Empty
            || sizeBytes < 0
            || string.IsNullOrWhiteSpace(displayName)
            || displayName.Length > 255
            || string.IsNullOrWhiteSpace(contentType)
            || contentType.Length > 128
            || maxForField < 1
            || maxPerSubmission < 1
        )
            return Result.Failure<FormAttachment>(FormRequestErrors.InvalidComponent);

        var result = _request
            .EnsureSubmission(nowUtc)
            .RegisterAttachment(
                _request.TenantId,
                address,
                fileId,
                displayName,
                contentType,
                sizeBytes,
                maxForField,
                maxPerSubmission,
                knownVerdict,
                _request.ExpectedFileOwners(),
                nowUtc
            );
        if (result.IsSuccess)
            _request.Touch(nowUtc);
        return result;
    }

    public Result Detach(Guid attachmentId, DateTime nowUtc)
    {
        var editable = _request.EnsureEditable();
        if (editable.IsFailure)
            return editable;
        if (_request.Submission is null)
            return Result.Failure(FormRequestErrors.AttachmentNotFound);
        var result = _request.Submission.DetachAttachment(attachmentId, nowUtc);
        if (result.IsSuccess)
            _request.Touch(nowUtc);
        return result;
    }

    public FileVerdictOutcome ApplyVerdict(Guid fileId, FormFileVerdict verdict, DateTime nowUtc)
    {
        if (_request.Submission is null)
            return FileVerdictOutcome.Ignored;

        var overall = FileVerdictOutcome.Ignored;
        foreach (var attachment in _request.Submission.Attachments)
        {
            if (attachment.FileId != fileId)
                continue;
            var previousStatus = attachment.Status;
            var outcome = attachment.ApplyVerdict(verdict, _request.ExpectedFileOwners(), nowUtc);
            if (outcome != FileVerdictOutcome.Ignored)
                overall = outcome;
            if (outcome == FileVerdictOutcome.Rejected && previousStatus != FormAttachmentStatus.Rejected)
                _request.Raise(
                    new FormAttachmentRejected(
                        _request.Id,
                        _request.TenantId,
                        attachment.Id,
                        fileId,
                        attachment.RejectionCode!,
                        nowUtc
                    )
                );
        }
        if (overall != FileVerdictOutcome.Ignored)
            _request.Touch(nowUtc);
        return overall;
    }

    public int ExpireStalePending(DateTime olderThanUtc, DateTime nowUtc)
    {
        if (_request.Submission is null)
            return 0;
        var expired = 0;
        foreach (var attachment in _request.Submission.Attachments)
        {
            if (attachment.Status != FormAttachmentStatus.Pending || attachment.RegisteredAtUtc >= olderThanUtc)
                continue;
            attachment.Expire(nowUtc);
            expired++;
            _request.Raise(
                new FormAttachmentRejected(
                    _request.Id,
                    _request.TenantId,
                    attachment.Id,
                    attachment.FileId,
                    attachment.RejectionCode!,
                    nowUtc
                )
            );
        }
        if (expired > 0)
            _request.Touch(nowUtc);
        return expired;
    }

    // Adjuntos vigentes que todavía no pasaron al cliente vinculado.
    public IReadOnlyList<Guid> PendingOwnerReassignment()
    {
        var files = new List<Guid>();
        if (
            _request.Submission is null
            || _request.ParticipantKind != FormParticipantKind.External
            || _request.CustomerLinkStatus != CustomerLinkStatus.Linked
        )
            return files;
        foreach (var attachment in _request.Submission.Attachments)
        {
            if (attachment.Status != FormAttachmentStatus.Detached && !attachment.OwnerReassignmentSettled)
                files.Add(attachment.FileId);
        }
        return files;
    }

    public Result RecordReassignment(Guid fileId, string outcome, DateTime nowUtc)
    {
        if (!FormFileReassignmentOutcomes.IsKnown(outcome) || _request.Submission is null)
            return Result.Failure(FormRequestErrors.AttachmentNotFound);
        var found = false;
        foreach (var attachment in _request.Submission.Attachments)
        {
            if (attachment.FileId != fileId || attachment.Status == FormAttachmentStatus.Detached)
                continue;
            attachment.RecordOwnerReassignment(outcome, nowUtc);
            found = true;
        }
        return found ? Result.Success() : Result.Failure(FormRequestErrors.AttachmentNotFound);
    }
}
