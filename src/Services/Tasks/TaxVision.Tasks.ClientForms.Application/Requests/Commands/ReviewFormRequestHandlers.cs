using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record ReturnFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    string Message,
    IReadOnlyList<FormFieldCommentInput> FieldComments,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record CompleteFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    string? InternalNote,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record CancelFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    string? Note,
    byte[] RowVersion,
    DateTime NowUtc
);

public sealed record ChangeFormRequestDueDateCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    DateTime? DueAtUtc,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class ReturnFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        ReturnFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(command.TenantId, command.ByUserId, command.CanViewAll, command.RequestId, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ConcurrencyConflict);
        var returned = found.Value.ReturnForChanges(command.Message, command.FieldComments, command.ByUserId, command.NowUtc);
        return await ReviewSupport.Save(returned, found.Value, "request.returned", command.ByUserId, audit, unitOfWork, ct);
    }
}

public static class CompleteFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        CompleteFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(command.TenantId, command.ByUserId, command.CanViewAll, command.RequestId, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ConcurrencyConflict);
        var completed = found.Value.Complete(command.InternalNote, command.ByUserId, command.NowUtc);
        return await ReviewSupport.Save(completed, found.Value, "request.completed", command.ByUserId, audit, unitOfWork, ct);
    }
}

public static class CancelFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        CancelFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(command.TenantId, command.ByUserId, command.CanViewAll, command.RequestId, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ConcurrencyConflict);
        var cancelled = found.Value.Cancel(command.Note, command.ByUserId, command.NowUtc);
        return await ReviewSupport.Save(cancelled, found.Value, "request.cancelled", command.ByUserId, audit, unitOfWork, ct);
    }
}

public static class ChangeFormRequestDueDateHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        ChangeFormRequestDueDateCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(command.TenantId, command.ByUserId, command.CanViewAll, command.RequestId, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        if (!FormRequestApplicationSupport.RowVersionMatches(found.Value, command.RowVersion))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ConcurrencyConflict);
        var changed = found.Value.ChangeDueDate(command.DueAtUtc, command.ByUserId, command.NowUtc);
        return await ReviewSupport.Save(changed, found.Value, "request.due_date_changed", command.ByUserId, audit, unitOfWork, ct);
    }
}

internal static class ReviewSupport
{
    internal static async Task<Result<FormRequest>> LoadVisible(
        Guid tenantId,
        Guid userId,
        bool canViewAll,
        Guid requestId,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var found = await requests.GetForReviewAsync(tenantId, requestId, ct);
        if (found.IsFailure || !await visibility.CanViewAsync(tenantId, userId, canViewAll, found.Value, ct))
            return Result.Failure<FormRequest>(FormRequestErrors.NotFound);
        return found;
    }

    internal static async Task<Result<FormRequestResponse>> Save(
        Result transition,
        FormRequest request,
        string action,
        Guid byUserId,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        if (transition.IsFailure)
            return Result.Failure<FormRequestResponse>(transition.Error);
        audit.Record(request.TenantId, request.FormTemplateId, request.Id, action, byUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestResponse.From(request));
    }
}
