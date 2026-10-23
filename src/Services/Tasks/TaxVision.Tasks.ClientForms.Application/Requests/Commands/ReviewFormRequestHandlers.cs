using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common;
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

    // RowVersion vigente y textos del staff sin identificadores fiscales ni de tarjeta.
    internal static Result EnsureWritable(FormRequest request, byte[] rowVersion, params string?[] staffTexts) =>
        FormRequestApplicationSupport.RowVersionMatches(request, rowVersion)
            ? StaffTextGuard.Validate(staffTexts)
            : Result.Failure(FormRequestErrors.ConcurrencyConflict);

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

    // Se mide después de guardar: una revisión que no confirma no cuenta.
    internal static async Task<Result<FormRequestResponse>> SaveReviewed(
        Result transition,
        FormRequest request,
        string action,
        string outcome,
        Guid byUserId,
        DateTime nowUtc,
        IFormAuditLog audit,
        IClientFormsMetrics metrics,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var saved = await Save(transition, request, action, byUserId, audit, unitOfWork, ct);
        if (saved.IsSuccess)
            metrics.RecordReviewed(outcome, nowUtc - request.LastSubmittedAtUtc);
        return saved;
    }
}
