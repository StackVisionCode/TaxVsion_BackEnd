using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record ReassignFormRequestResponsibleCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid RequestId,
    Guid UserId,
    byte[] RowVersion,
    DateTime NowUtc
);

public static class ReassignFormRequestResponsibleHandler
{
    private const string ReviewPermission = "tasks.form_requests.review";

    public static async Task<Result<FormRequestResponse>> Handle(
        ReassignFormRequestResponsibleCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormResponsibleUserValidator responsibleUsers,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.RequestId,
            requests,
            visibility,
            ct
        );
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);
        var valid = await ValidateAsync(command, found.Value, responsibleUsers, ct);
        if (valid.IsFailure)
            return Result.Failure<FormRequestResponse>(valid.Error);
        return await ReviewSupport.Save(
            found.Value.AssignResponsible(command.UserId, command.NowUtc),
            found.Value,
            "request.responsible_changed",
            command.ByUserId,
            audit,
            unitOfWork,
            ct
        );
    }

    private static async Task<Result> ValidateAsync(
        ReassignFormRequestResponsibleCommand command,
        FormRequest request,
        IFormResponsibleUserValidator responsibleUsers,
        CancellationToken ct
    )
    {
        if (!FormRequestApplicationSupport.RowVersionMatches(request, command.RowVersion))
            return Result.Failure(FormRequestErrors.ConcurrencyConflict);
        var active = await responsibleUsers.IsActiveAsync(command.TenantId, command.UserId, ct);
        var allowed = await responsibleUsers.HasPermissionsAsync(
            command.TenantId,
            command.UserId,
            [ReviewPermission],
            ct
        );
        return active && allowed ? Result.Success() : Result.Failure(FormRequestErrors.ResponsibleNotAuthorized);
    }
}
