using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Templates;
using TaxVision.Tasks.ClientForms.Domain.Versions;
using Wolverine;

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
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var input = ValidateInput(command);
        var assignable = input.IsFailure
            ? input
            : await EnsureCustomerAssignableAsync(command, customers, visibility, ct);
        if (assignable.IsFailure)
            return Result.Failure<FormRequestResponse>(assignable.Error);
        var version = await LoadPublishedVersionAsync(command, templates, versions, ct);
        if (version.IsFailure)
            return Result.Failure<FormRequestResponse>(version.Error);
        var free = await EnsureNoConflictsAsync(command, requests, tasks, ct);
        if (free.IsFailure)
            return Result.Failure<FormRequestResponse>(free.Error);
        var created = Create(command, version.Value);
        if (created.IsFailure)
            return Result.Failure<FormRequestResponse>(created.Error);

        requests.Add(created.Value);
        audit.Record(command.TenantId, command.TemplateId, created.Value.Id, "request.assigned", command.ByUserId);
        await bus.PublishAsync(FormRequestIntegrationEventFactory.Assigned(created.Value, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(FormRequestResponse.From(created.Value));
    }

    // El prellenado todavía no existe: un id de origen se rechaza en vez de guardarse sin validar.
    private static Result ValidateInput(AssignFormRequestCommand command) =>
        command.PrefillFromRequestId.HasValue
            ? Result.Failure(FormRequestErrors.PrefillNotAvailable)
            : StaffTextGuard.Validate(command.Title, command.MessageToClient);

    private static async Task<Result> EnsureCustomerAssignableAsync(
        AssignFormRequestCommand command,
        IClientFormsCustomerReader customers,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var customer = await customers.GetAsync(command.TenantId, command.CustomerId, ct);
        if (customer is null)
            return Result.Failure(FormRequestErrors.NotFound);
        if (!customer.IsActive)
            return Result.Failure(FormRequestErrors.CustomerUnavailable);
        var visible = await visibility.CanViewCustomerAsync(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.CustomerId,
            ct
        );
        return visible ? Result.Success() : Result.Failure(FormRequestErrors.NotFound);
    }

    private static async Task<Result<FormTemplateVersion>> LoadPublishedVersionAsync(
        AssignFormRequestCommand command,
        IFormTemplateRepository templates,
        IFormTemplateVersionRepository versions,
        CancellationToken ct
    )
    {
        var template = await templates.GetByIdAsync(command.TenantId, command.TemplateId, ct);
        if (
            template.IsFailure
            || template.Value.Status != FormTemplateStatus.Active
            || !template.Value.LatestVersionId.HasValue
        )
            return Result.Failure<FormTemplateVersion>(FormRequestErrors.TemplateNotPublished);
        var version = await versions.GetByIdAsync(command.TenantId, template.Value.LatestVersionId.Value, ct);
        return version.IsFailure
            ? Result.Failure<FormTemplateVersion>(FormRequestErrors.TemplateNotPublished)
            : Result.Success(version.Value);
    }

    private static async Task<Result> EnsureNoConflictsAsync(
        AssignFormRequestCommand command,
        IFormRequestRepository requests,
        IClientFormsTaskReader tasks,
        CancellationToken ct
    )
    {
        if (await requests.HasOpenRequestAsync(command.TenantId, command.CustomerId, command.TemplateId, null, ct))
            return Result.Failure(FormRequestErrors.AlreadyOpenForTemplate);
        if (
            command.TaskId.HasValue
            && !await tasks.BelongsToCustomerAsync(command.TenantId, command.TaskId.Value, command.CustomerId, ct)
        )
            return Result.Failure(FormRequestErrors.TaskMismatch);
        return Result.Success();
    }

    private static Result<FormRequest> Create(AssignFormRequestCommand command, FormTemplateVersion version) =>
        FormRequest.Create(
            command.TenantId,
            FormParticipantRef.Customer(command.CustomerId),
            version,
            command.Title,
            command.MessageToClient,
            command.TaxYear,
            command.TaskId,
            command.DueAtUtc,
            null,
            null,
            FormRequestOrigin.Directed,
            null,
            null,
            command.ByUserId,
            command.NowUtc
        );
}
