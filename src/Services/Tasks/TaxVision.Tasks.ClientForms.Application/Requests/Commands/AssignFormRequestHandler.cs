using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public sealed record AssignFormRequestCommand(
    Guid TenantId,
    Guid ByUserId,
    bool CanViewAll,
    Guid CustomerId,
    Guid TemplateId,
    string? Title,
    string? MessageToClient,
    DateTime? DueAtUtc,
    int? TaxYear,
    Guid? TaskId,
    Guid? PrefillFromRequestId,
    DateTime NowUtc
);

public static class AssignFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        AssignFormRequestCommand command,
        IClientFormsCustomerReader customers,
        IClientFormsTaskReader tasks,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        IFormRequestRepository requests,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var customer = await customers.GetAsync(command.TenantId, command.CustomerId, ct);
        if (customer is null)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.NotFound);
        if (!customer.IsActive)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.CustomerUnavailable);
        if (!await visibility.CanViewCustomerAsync(
                command.TenantId,
                command.ByUserId,
                command.CanViewAll,
                command.CustomerId,
                ct
            ))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.NotFound);

        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (template.IsFailure || template.Value.Status != FormTemplateStatus.Active || !template.Value.LatestVersionId.HasValue)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.TemplateNotPublished);
        var version = await versions.GetByIdAsync(command.TenantId, template.Value.LatestVersionId.Value, ct);
        if (version.IsFailure)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.TemplateNotPublished);
        if (await requests.HasOpenRequestAsync(command.TenantId, command.CustomerId, command.TemplateId, null, ct))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.AlreadyOpenForTemplate);
        if (command.TaskId.HasValue
            && !await tasks.BelongsToCustomerAsync(command.TenantId, command.TaskId.Value, command.CustomerId, ct))
            return Result.Failure<FormRequestResponse>(FormRequestErrors.TaskMismatch);

        var created = FormRequest.Create(
            command.TenantId,
            FormParticipantRef.Customer(command.CustomerId),
            version.Value,
            command.Title,
            command.MessageToClient,
            command.TaxYear,
            command.TaskId,
            command.DueAtUtc,
            command.PrefillFromRequestId,
            null,
            FormRequestOrigin.Directed,
            null,
            null,
            command.ByUserId,
            command.NowUtc
        );
        if (created.IsFailure)
            return Result.Failure<FormRequestResponse>(created.Error);

        requests.Add(created.Value);
        audit.Record(command.TenantId, command.TemplateId, created.Value.Id, "request.assigned", command.ByUserId);
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestResponse.From(created.Value));
    }
}
