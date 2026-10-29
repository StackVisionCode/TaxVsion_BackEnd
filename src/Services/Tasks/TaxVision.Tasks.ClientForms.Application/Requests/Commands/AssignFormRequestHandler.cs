using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
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
        IFormEngine engine,
        IAnswersProtector answersProtector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var text = StaffTextGuard.Validate(command.Title, command.MessageToClient);
        if (text.IsFailure)
            return Result.Failure<FormRequestResponse>(text.Error);
        var customer = await EnsureCustomerAssignableAsync(command, customers, visibility, ct);
        if (customer.IsFailure)
            return Result.Failure<FormRequestResponse>(customer.Error);
        var version = await LoadPublishedVersionAsync(command, templates, versions, ct);
        if (version.IsFailure)
            return Result.Failure<FormRequestResponse>(version.Error);
        var free = await EnsureNoConflictsAsync(command, requests, tasks, ct);
        if (free.IsFailure)
            return Result.Failure<FormRequestResponse>(free.Error);

        return await CreateAndPersistAsync(
            command,
            version.Value,
            customer.Value,
            versions,
            requests,
            engine,
            answersProtector,
            audit,
            unitOfWork,
            bus,
            correlation,
            metrics,
            ct
        );
    }

    private static async Task<Result<FormRequestResponse>> CreateAndPersistAsync(
        AssignFormRequestCommand command,
        FormTemplateVersion version,
        ClientFormsCustomerRecord customer,
        IFormTemplateVersionRepository versions,
        IFormRequestRepository requests,
        IFormEngine engine,
        IAnswersProtector answersProtector,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        IClientFormsMetrics metrics,
        CancellationToken ct
    )
    {
        var prefill = await BuildPrefillAsync(
            command,
            version,
            customer,
            versions,
            requests,
            engine,
            answersProtector,
            ct
        );
        if (prefill.IsFailure)
            return Result.Failure<FormRequestResponse>(prefill.Error);
        var created = Create(command, version, prefill.Value);
        if (created.IsFailure)
            return Result.Failure<FormRequestResponse>(created.Error);

        requests.Add(created.Value);
        audit.Record(command.TenantId, command.TemplateId, created.Value.Id, "request.assigned", command.ByUserId);
        // Guardar antes de publicar (T-8): un choque al guardar (p. ej. el índice de request abierta) no deja
        // salir el aviso de una asignación que no existe.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(FormRequestIntegrationEventFactory.Assigned(created.Value, correlation.CorrelationId));
        metrics.RecordRequestAssigned();
        return Result.Success(FormRequestResponse.From(created.Value));
    }

    private static async Task<Result<ClientFormsCustomerRecord>> EnsureCustomerAssignableAsync(
        AssignFormRequestCommand command,
        IClientFormsCustomerReader customers,
        IFormRequestVisibilityFilter visibility,
        CancellationToken ct
    )
    {
        var customer = await customers.GetAsync(command.TenantId, command.CustomerId, ct);
        if (customer is null)
            return Result.Failure<ClientFormsCustomerRecord>(FormRequestErrors.NotFound);
        if (!customer.IsActive)
            return Result.Failure<ClientFormsCustomerRecord>(FormRequestErrors.CustomerUnavailable);
        var visible = await visibility.CanViewCustomerAsync(
            command.TenantId,
            command.ByUserId,
            command.CanViewAll,
            command.CustomerId,
            ct
        );
        return visible
            ? Result.Success(customer)
            : Result.Failure<ClientFormsCustomerRecord>(FormRequestErrors.NotFound);
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

    // P-13.1 — Prellenado "from last year": copia desde la última request Completed del mismo template y
    // cliente; taxId y file nunca se copian. Sin toggle no hay prellenado.
    private static async Task<Result<PrefillResult>> BuildPrefillAsync(
        AssignFormRequestCommand command,
        FormTemplateVersion targetVersion,
        ClientFormsCustomerRecord customer,
        IFormTemplateVersionRepository versions,
        IFormRequestRepository requests,
        IFormEngine engine,
        IAnswersProtector answersProtector,
        CancellationToken ct
    )
    {
        if (!command.PrefillFromRequestId.HasValue)
            return Result.Success(PrefillResult.None);

        var source = await requests.GetForReviewAsync(command.TenantId, command.PrefillFromRequestId.Value, ct);
        if (
            source.IsFailure
            || source.Value.EffectiveCustomerId != command.CustomerId
            || source.Value.FormTemplateId != command.TemplateId
            || source.Value.Status != FormRequestStatus.Completed
            || source.Value.LatestRevision is null
        )
            return Result.Failure<PrefillResult>(FormRequestErrors.PrefillNotAvailable);

        return await BuildFromSourceAsync(
            command,
            targetVersion,
            customer,
            source.Value,
            versions,
            engine,
            answersProtector,
            ct
        );
    }

    private static async Task<Result<PrefillResult>> BuildFromSourceAsync(
        AssignFormRequestCommand command,
        FormTemplateVersion targetVersion,
        ClientFormsCustomerRecord customer,
        FormRequest source,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IAnswersProtector answersProtector,
        CancellationToken ct
    )
    {
        var sourceVersion = await versions.GetByIdAsync(command.TenantId, source.FormTemplateVersionId, ct);
        if (sourceVersion.IsFailure)
            return Result.Failure<PrefillResult>(FormRequestErrors.PrefillNotAvailable);

        var sourceDefinition = engine.ParseDefinition(sourceVersion.Value.Definition.Json);
        var targetDefinition = engine.ParseDefinition(targetVersion.Definition.Json);
        var sourceAnswers = engine.ParseAnswers(answersProtector.Unprotect(source.LatestRevision!.Answers));
        if (sourceDefinition.IsFailure || targetDefinition.IsFailure || sourceAnswers.IsFailure)
            return Result.Failure<PrefillResult>(FormRequestErrors.PrefillNotAvailable);

        var (draft, prefilledComponentIds) = FormPrefillBuilder.Build(
            sourceDefinition.Value,
            sourceAnswers.Value,
            targetDefinition.Value,
            customer.DisplayName
        );
        if (prefilledComponentIds.Count == 0)
            return Result.Failure<PrefillResult>(FormRequestErrors.PrefillNotAvailable);

        var protectedDraft = answersProtector.Protect(FormRequestApplicationSupport.Serialize(draft));
        return Result.Success(new PrefillResult(source.Id, protectedDraft, prefilledComponentIds));
    }

    private static Result<FormRequest> Create(
        AssignFormRequestCommand command,
        FormTemplateVersion version,
        PrefillResult prefill
    ) =>
        FormRequest.Create(
            command.TenantId,
            FormParticipantRef.Customer(command.CustomerId),
            version,
            command.Title,
            command.MessageToClient,
            command.TaxYear,
            command.TaskId,
            command.DueAtUtc,
            prefill.SourceRequestId,
            prefill.Draft,
            FormRequestOrigin.Directed,
            null,
            null,
            command.ByUserId,
            command.NowUtc,
            prefill.PrefilledComponentIds
        );

    private sealed record PrefillResult(
        Guid? SourceRequestId,
        Domain.Requests.ProtectedAnswers? Draft,
        IReadOnlyList<string> PrefilledComponentIds
    )
    {
        public static readonly PrefillResult None = new(null, null, []);
    }
}
