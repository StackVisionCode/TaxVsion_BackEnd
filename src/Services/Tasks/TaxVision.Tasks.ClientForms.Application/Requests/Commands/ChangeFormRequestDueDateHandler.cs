using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

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
