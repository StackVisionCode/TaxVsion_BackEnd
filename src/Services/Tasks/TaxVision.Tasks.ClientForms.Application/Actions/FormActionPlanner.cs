using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Common;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

/// <summary>
/// El run creado y el mensaje que su handler publica después de guardar.
/// </summary>
public sealed record FormActionEmission(FormActionRun Run, object Message);

/// <summary>
/// Tabla de estados de las acciones: crea los runs y entrega sus comandos al handler que la invoca, que
/// guarda y después publica; así run, comando y transición se confirman en la misma transacción del outbox.
/// </summary>
public sealed class FormActionPlanner(
    IFormTemplateVersionRepository versions,
    IFormEngine engine,
    IDataCollectionTargetCatalog catalog,
    IFormModuleSettingsRepository settings,
    IAnswersProtector protector,
    IProtectedPayloadCodec codec,
    ICorrelationContext correlation
)
{
    // Devuelve la foto previa de los runs: quien guarda mide lo planificado una vez confirmado.
    // Los comandos van a `outgoing`: quien llama los publica después de guardar (T-8).
    public async Task<FormActionRunTally> PlanAsync(
        FormRequest request,
        FormActionTrigger trigger,
        FormAnswers? answers,
        DeferredMessages outgoing,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var tally = FormActionRunTally.Of(request);
        var context = await LoadAsync(request, ct);
        if (context is null)
            return tally;
        foreach (var action in context.Rules.Actions)
        {
            if (action.Trigger != trigger || action.Type is FormActionType.NotifyStaff or FormActionType.RouteToReview)
                continue;
            if (action.Type == FormActionType.CreateCustomer)
                await PlanCustomerCreationAsync(request, action, context, answers, outgoing, nowUtc, ct);
            else
                Skip(request, action, FormActionRunErrors.NotSupportedCode, nowUtc);
        }
        return tally;
    }

    // P-18 — la firma de la revisión terminó: completada ejecuta el alta que la esperaba (waitFor), si no
    // queda Skipped(signature_not_completed). Idempotente ante eventos repetidos. No publica: el consumer
    // guarda y después publica lo que deja en `outgoing` (T-8).
    public async Task<FormActionRunTally> PlanAfterSignatureAsync(
        FormRequest request,
        int revisionNumber,
        bool signatureCompleted,
        DeferredMessages outgoing,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        var tally = FormActionRunTally.Of(request);
        // Una firma de una revisión vieja (p.ej. cancelada por un reenvío) no decide nada.
        if (
            revisionNumber != request.CurrentRevisionNumber
            || !AwaitsCustomer(request)
            || request.HasActiveAction(FormActionNames.CreateCustomer)
        )
            return tally;
        var context = await LoadAsync(request, ct);
        var action = context?.Rules.Actions.FirstOrDefault(item =>
            item.Type == FormActionType.CreateCustomer
            && item.Trigger != FormActionTrigger.Manual
            && WaitsForSignature(item)
        );
        if (context is null || action is null)
            return tally;
        if (!signatureCompleted)
        {
            // Un rechazo tardío no deshace una firma que ya se completó o selló.
            if (
                request.RevisionSignatureStatus(revisionNumber)
                is FormRevisionSignatureStatus.Completed
                    or FormRevisionSignatureStatus.Sealed
            )
                return tally;
            SkipOnce(request, action, FormActionRunErrors.SignatureNotCompletedCode, nowUtc);
            return tally;
        }
        var tenantSettings = await settings.GetAsync(request.TenantId, ct);
        if (!FormModuleCapabilities.Of(tenantSettings).AutoCustomerCreationEnabled)
            return tally;
        var answers = LatestAnswers(request);
        if (answers is null || !engine.EvaluateCondition(context.Definition, answers, action.Condition))
        {
            SkipOnce(request, action, FormActionRunErrors.ConditionNotMetCode, nowUtc);
            return tally;
        }
        var emission = Emit(request, action, context, answers, null, nowUtc);
        if (emission.IsSuccess)
            outgoing.Add(emission.Value.Message);
        return tally;
    }

    // Arranque manual o reintento. runId null: el último intento, si lo hay, debe admitir reintento.
    // No publica: el handler guarda primero y publica después (T-8). Si el guardado choca, Wolverine
    // reintenta el handler en línea y un comando ya publicado saldría igual aunque su run se revirtió.
    public async Task<Result<FormActionEmission>> RequestCustomerCreationAsync(
        FormRequest request,
        Guid? runId,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!AwaitsCustomer(request))
            return Result.Failure<FormActionEmission>(FormActionRunErrors.NotAvailable);
        var previous = ResolvePrevious(request, runId);
        if (previous.IsFailure)
            return Result.Failure<FormActionEmission>(previous.Error);
        var context = await LoadAsync(request, ct);
        var action = context?.Rules.Actions.FirstOrDefault(item => item.Type == FormActionType.CreateCustomer);
        if (context is null || action is null)
            return Result.Failure<FormActionEmission>(FormActionRunErrors.NotAvailable);
        return Emit(request, action, context, LatestAnswers(request), previous.Value, nowUtc);
    }

    // Una request crea como máximo un cliente: no se vuelve a planificar en cada revisión.
    private async Task PlanCustomerCreationAsync(
        FormRequest request,
        FormAction action,
        FormActionContext context,
        FormAnswers? answers,
        DeferredMessages outgoing,
        DateTime nowUtc,
        CancellationToken ct
    )
    {
        if (!AwaitsCustomer(request) || request.HasActiveAction(FormActionNames.CreateCustomer))
            return;
        var tenantSettings = await settings.GetAsync(request.TenantId, ct);
        if (!FormModuleCapabilities.Of(tenantSettings).AutoCustomerCreationEnabled)
            return;
        if (WaitsForSignature(action))
        {
            // Espera la firma de la revisión: la planifica PlanAfterSignatureAsync. Sin paso de firma no llegaría nunca.
            if (!HasSignatureStep(context.Definition))
                Skip(request, action, FormActionRunErrors.SignatureNotConfiguredCode, nowUtc);
        }
        else if (answers is null || !engine.EvaluateCondition(context.Definition, answers, action.Condition))
            Skip(request, action, FormActionRunErrors.ConditionNotMetCode, nowUtc);
        else
        {
            var emission = Emit(request, action, context, answers, null, nowUtc);
            if (emission.IsSuccess)
                outgoing.Add(emission.Value.Message);
        }
    }

    private Result<FormActionEmission> Emit(
        FormRequest request,
        FormAction action,
        FormActionContext context,
        FormAnswers? answers,
        FormActionRun? previous,
        DateTime nowUtc
    )
    {
        var payload = answers is null
            ? Result.Failure<CustomerCreationFromFormPayload>(FormActionRunErrors.Invalid)
            : CustomerCreationPayloadBuilder.Build(context.Rules, action, catalog.GetSnapshot(), answers);
        Guid? commandId = payload.IsSuccess ? Guid.NewGuid() : null;
        var failure = payload.IsSuccess ? null : FormActionRunErrors.PayloadUnavailableCode;
        var run = previous is null
            ? NewRun(request, action, commandId, failure, nowUtc)
            : request.RetryAction(previous.Id, commandId, failure, nowUtc);
        if (run.IsFailure)
            return Result.Failure<FormActionEmission>(run.Error);
        object message = payload.IsFailure
            ? CustomerCreationCommandFactory.Failed(request, run.Value.ActionType, failure!, correlation.CorrelationId)
            : CustomerCreationCommandFactory.Create(
                request,
                run.Value,
                context.Version.TargetCatalogVersion,
                payload.Value,
                codec,
                correlation.CorrelationId
            );
        return Result.Success(new FormActionEmission(run.Value, message));
    }

    private static Result<FormActionRun> NewRun(
        FormRequest request,
        FormAction action,
        Guid? commandId,
        string? failure,
        DateTime nowUtc
    ) =>
        commandId is { } id
            ? request.RequestAction(action.Key, FormActionNames.CreateCustomer, id, nowUtc)
            : request.CloseActionWithoutCommand(
                action.Key,
                FormActionNames.CreateCustomer,
                FormActionRunStatus.Failed,
                failure!,
                nowUtc
            );

    private static Result<FormActionRun?> ResolvePrevious(FormRequest request, Guid? runId)
    {
        if (runId is { } id)
        {
            var run = request.GetRetryableAction(id);
            if (run.IsFailure)
                return Result.Failure<FormActionRun?>(run.Error);
            return run.Value.ActionType == FormActionNames.CreateCustomer
                ? Result.Success<FormActionRun?>(run.Value)
                : Result.Failure<FormActionRun?>(FormActionRunErrors.NotRetryable);
        }
        var latest = request.LatestAction(FormActionNames.CreateCustomer);
        if (latest is null)
            return Result.Success<FormActionRun?>(null);
        return latest.IsRetryable
            ? Result.Success<FormActionRun?>(latest)
            : Result.Failure<FormActionRun?>(FormActionRunErrors.NotRetryable);
    }

    private async Task<FormActionContext?> LoadAsync(FormRequest request, CancellationToken ct)
    {
        var version = await versions.GetByIdAsync(request.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure || version.Value.ActionSetJson == "[]")
            return null;
        var rules = engine.ParseRuleSets(version.Value.MappingSetJson, version.Value.ActionSetJson);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        return rules.IsFailure || definition.IsFailure
            ? null
            : new FormActionContext(version.Value, definition.Value, rules.Value);
    }

    private FormAnswers? LatestAnswers(FormRequest request)
    {
        var revision = request.LatestRevision;
        if (revision is null)
            return null;
        var answers = engine.ParseAnswers(protector.Unprotect(revision.Answers));
        return answers.IsSuccess ? answers.Value : null;
    }

    private static bool AwaitsCustomer(FormRequest request) =>
        request.ParticipantKind == FormParticipantKind.External
        && request.CustomerLinkStatus == CustomerLinkStatus.None
        && request.ResponsibleUserId.HasValue
        && request.CurrentRevisionNumber > 0;

    // config.waitFor = "signatureCompleted": el alta espera a que la firma de la revisión se complete.
    private static bool WaitsForSignature(FormAction action) =>
        action.Config.ValueKind == System.Text.Json.JsonValueKind.Object
        && action.Config.TryGetProperty("waitFor", out var waitFor)
        && waitFor.ValueKind == System.Text.Json.JsonValueKind.String
        && waitFor.GetString() == "signatureCompleted";

    private static bool HasSignatureStep(FormDefinition definition) =>
        definition
            .Pages.SelectMany(page => page.Sections)
            .SelectMany(section => section.Components)
            .Any(component => string.Equals(component.Type, "signatureStep", StringComparison.Ordinal));

    private static void SkipOnce(FormRequest request, FormAction action, string reason, DateTime nowUtc)
    {
        if (!request.HasClosedAction(action.Key, request.CurrentRevisionNumber, reason))
            Skip(request, action, reason, nowUtc);
    }

    private static void Skip(FormRequest request, FormAction action, string reason, DateTime nowUtc) =>
        request.CloseActionWithoutCommand(
            action.Key,
            action.Type.ToString(),
            FormActionRunStatus.Skipped,
            reason,
            nowUtc
        );
}
