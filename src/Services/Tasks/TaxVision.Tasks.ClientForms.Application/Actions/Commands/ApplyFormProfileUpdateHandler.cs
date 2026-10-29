using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using Wolverine;

namespace TaxVision.Tasks.ClientForms.Application.Actions.Commands;

/// <summary>
/// P-13.2 — Autoriza ANTES de emitir (apply_profile + customers.manage + ABAC ya en el controller; aquí se
/// re-chequea el gate de admin para destinos fiscales). Si algo falla, no se emite nada. Guarda antes de publicar.
/// </summary>
public static class ApplyFormProfileUpdateHandler
{
    private const string Action = "profile_update.requested";

    public static async Task<Result<FormRequestResponse>> Handle(
        ApplyFormProfileUpdateCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IDataCollectionTargetCatalog catalog,
        IAnswersProtector answersProtector,
        ISensitiveValueProtector sensitiveProtector,
        IProtectedPayloadCodec codec,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var found = await LoadCompletedAsync(command, requests, visibility, ct);
        if (found.IsFailure)
            return Result.Failure<FormRequestResponse>(found.Error);

        var plan = await PlanAsync(
            command,
            found.Value,
            versions,
            engine,
            catalog,
            answersProtector,
            sensitiveProtector,
            ct
        );
        if (plan.IsFailure)
            return Result.Failure<FormRequestResponse>(plan.Error);
        if (plan.Value.HasFiscal && !command.IsTenantAdmin && !command.IsPlatformAdmin)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ProfileUpdateForbidden);
        if (plan.Value.Values.Count == 0)
            return Result.Failure<FormRequestResponse>(FormRequestErrors.ProfileUpdateNoTargets);

        return await EmitAsync(command, found.Value, plan.Value, codec, audit, unitOfWork, bus, correlation, ct);
    }

    private static async Task<Result<FormRequest>> LoadCompletedAsync(
        ApplyFormProfileUpdateCommand command,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
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
            return found;
        return found.Value.Status == FormRequestStatus.Completed && found.Value.LatestRevision is not null
            ? found
            : Result.Failure<FormRequest>(FormRequestErrors.ProfileUpdateNotAllowed);
    }

    private static async Task<Result<FormRequestResponse>> EmitAsync(
        ApplyFormProfileUpdateCommand command,
        FormRequest request,
        ProfileUpdatePlan plan,
        IProtectedPayloadCodec codec,
        IFormAuditLog audit,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var commandId = Guid.NewGuid();
        var requested = request.RequestProfileUpdate(commandId, plan.TargetKeys, command.NowUtc);
        if (requested.IsFailure)
            return Result.Failure<FormRequestResponse>(requested.Error);

        var message = CustomerProfileUpdateCommandFactory.Create(
            request,
            commandId,
            commandId.ToString("N"),
            plan.TargetCatalogVersion,
            new CustomerProfileUpdateFromFormPayload(command.ByUserId, command.ActorType, plan.Values),
            codec,
            correlation.CorrelationId
        );
        // Audit con los destinos, SIN valores.
        audit.Record(
            command.TenantId,
            request.FormTemplateId,
            request.Id,
            Action,
            command.ByUserId,
            JsonSerializer.Serialize(new { targets = plan.TargetKeys })
        );
        // Guardar antes de publicar: un conflicto no deja un comando suelto.
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(message);
        return Result.Success(FormRequestResponse.From(request, command.NowUtc));
    }

    private sealed record ProfileUpdatePlan(
        IReadOnlyList<ClientFormTargetValue> Values,
        IReadOnlyList<string> TargetKeys,
        bool HasFiscal,
        int TargetCatalogVersion
    );

    private static async Task<Result<ProfileUpdatePlan>> PlanAsync(
        ApplyFormProfileUpdateCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IDataCollectionTargetCatalog catalog,
        IAnswersProtector answersProtector,
        ISensitiveValueProtector sensitiveProtector,
        CancellationToken ct
    )
    {
        var built = await BuildDraftAsync(command, request, versions, engine, catalog, answersProtector, ct);
        if (built.IsFailure)
            return Result.Failure<ProfileUpdatePlan>(built.Error);

        var draft = built.Value.Draft;
        var values = new List<ClientFormTargetValue>(draft.Values);
        var targetKeys = new List<string>(draft.TargetKeys);
        var hasFiscal = draft.HasFiscalTarget;
        if (command.IncludeTaxIdentifier && draft.TaxIdComponentId is { } taxIdComponentId)
        {
            var tax = ResolveTaxIdentifier(
                request,
                taxIdComponentId,
                answersProtector,
                sensitiveProtector,
                draft.TaxIdTargetSchemaVersion
            );
            if (tax is not null)
            {
                values.Add(tax);
                targetKeys.Add(tax.TargetKey);
                hasFiscal = true;
            }
        }
        return Result.Success(new ProfileUpdatePlan(values, targetKeys, hasFiscal, built.Value.CatalogVersion));
    }

    private static async Task<
        Result<(CustomerProfileUpdatePayloadBuilder.ProfileUpdateDraft Draft, int CatalogVersion)>
    > BuildDraftAsync(
        ApplyFormProfileUpdateCommand command,
        FormRequest request,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IDataCollectionTargetCatalog catalog,
        IAnswersProtector answersProtector,
        CancellationToken ct
    )
    {
        var version = await versions.GetByIdAsync(command.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Failure<(CustomerProfileUpdatePayloadBuilder.ProfileUpdateDraft, int)>(
                FormRequestErrors.ProfileUpdateNotAllowed
            );
        var rules = engine.ParseRuleSets(version.Value.MappingSetJson, version.Value.ActionSetJson);
        var answers = engine.ParseAnswers(answersProtector.Unprotect(request.LatestRevision!.Answers));
        if (rules.IsFailure || answers.IsFailure)
            return Result.Failure<(CustomerProfileUpdatePayloadBuilder.ProfileUpdateDraft, int)>(
                FormRequestErrors.ProfileUpdateNotAllowed
            );
        var draft = CustomerProfileUpdatePayloadBuilder.Build(
            rules.Value,
            catalog.GetSnapshot(),
            answers.Value,
            command.ComponentIds
        );
        return draft.IsFailure
            ? Result.Failure<(CustomerProfileUpdatePayloadBuilder.ProfileUpdateDraft, int)>(draft.Error)
            : Result.Success((draft.Value, version.Value.TargetCatalogVersion));
    }

    // El texto plano del SSN/EIN existe solo aquí, en memoria; nunca se registra. Solo si una revisión lo referencia.
    private static ClientFormTargetValue? ResolveTaxIdentifier(
        FormRequest request,
        string componentId,
        IAnswersProtector answersProtector,
        ISensitiveValueProtector sensitiveProtector,
        int targetSchemaVersion
    )
    {
        var answer = request.Submission?.SensitiveAnswers.FirstOrDefault(item =>
            string.Equals(item.ComponentId, componentId, StringComparison.Ordinal)
            && item.RowId is null
            && item.SupersededAtUtc is null
        );
        if (answer is null)
            return null;
        var referenced = new HashSet<Guid>();
        foreach (var revision in request.Submission!.Revisions)
            FormSensitiveReferenceReader.Collect(answersProtector.Unprotect(revision.Answers), referenced);
        if (!referenced.Contains(answer.Id))
            return null;

        var plaintext = sensitiveProtector.Unprotect(
            new ProtectedSensitiveValue(answer.Protected.Cipher, answer.Protected.KeyVersion)
        );
        return new ClientFormTargetValue(
            "customer.fiscal.tax_identifier",
            targetSchemaVersion,
            plaintext,
            Kind: answer.Kind.ToString()
        );
    }
}
