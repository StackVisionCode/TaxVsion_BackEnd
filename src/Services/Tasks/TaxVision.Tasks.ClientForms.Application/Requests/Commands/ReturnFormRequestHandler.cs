using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Commands;

public static class ReturnFormRequestHandler
{
    public static async Task<Result<FormRequestResponse>> Handle(
        ReturnFormRequestCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
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
        var valid = await ValidateFeedbackAsync(command, found.Value, versions, engine, protector, ct);
        if (valid.IsFailure)
            return Result.Failure<FormRequestResponse>(valid.Error);
        var returned = ReturnForChanges(found.Value, command);
        if (returned.IsSuccess)
            await PublishReturnedAsync(found.Value, command.ByUserId, bus, correlation);
        return await ReviewSupport.Save(
            returned,
            found.Value,
            "request.returned",
            command.ByUserId,
            audit,
            unitOfWork,
            ct
        );
    }

    // RowVersion vigente, textos sin identificadores y cada comentario sobre un campo de la versión fijada.
    private static async Task<Result> ValidateFeedbackAsync(
        ReturnFormRequestCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector protector,
        CancellationToken ct
    )
    {
        var safe = ReviewSupport.EnsureWritable(
            request,
            command.RowVersion,
            [command.Message, .. command.FieldComments.Select(item => item.Comment)]
        );
        if (safe.IsFailure || command.FieldComments.Count == 0)
            return safe;
        var revision = request.Submission?.Revisions.FirstOrDefault(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        if (revision is null)
            return Result.Success();
        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        var definition = version.IsFailure ? null : engine.ParseDefinition(version.Value.Definition.Json);
        var answers = engine.ParseAnswers(protector.Unprotect(revision.Answers));
        if (definition is null || definition.IsFailure || answers.IsFailure)
            return Result.Failure(FormRequestErrors.InvalidComponent);
        return command.FieldComments.All(item => TargetsField(item, definition.Value, answers.Value))
            ? Result.Success()
            : Result.Failure(FormRequestErrors.InvalidComponent);
    }

    // En un repeater el comentario apunta a una fila que existe en esa revisión; fuera de él, sin fila.
    private static bool TargetsField(FormFieldCommentInput comment, FormDefinition definition, FormAnswers answers)
    {
        var component = FormRequestApplicationSupport.FindComponent(definition, comment.Address.ComponentId);
        if (component is null)
            return false;
        if (!component.Value.Repeated)
            return comment.Address.RowId is null;
        return comment.Address.RowId is { } rowId
            && FormRequestApplicationSupport.HasRow(answers, definition, comment.Address.ComponentId, rowId);
    }

    private static Result ReturnForChanges(FormRequest request, ReturnFormRequestCommand command) =>
        request.ReturnForChanges(command.Message, command.FieldComments, command.ByUserId, command.NowUtc);

    private static async Task PublishReturnedAsync(
        FormRequest request,
        Guid byUserId,
        IMessageBus bus,
        ICorrelationContext correlation
    )
    {
        var revision = request.Submission!.Revisions.Single(item =>
            item.RevisionNumber == request.CurrentRevisionNumber
        );
        await bus.PublishAsync(
            FormRequestIntegrationEventFactory.Returned(request, revision, byUserId, correlation.CorrelationId)
        );
    }
}
