using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

internal static class FormConsentComponentResolver
{
    // Solo se decide sobre un consent visible de una request que todavía admite respuestas.
    internal static async Task<Result<ResolvedFormConsent>> ResolveAsync(
        ParticipantContext context,
        Guid requestId,
        string componentId,
        FormConsentRuntime runtime,
        CancellationToken ct
    )
    {
        var request = await runtime.Requests.GetForCommandAsync(context.TenantId, requestId, ct);
        if (request.IsFailure || !FormRequestApplicationSupport.Matches(context, request.Value))
            return Failure(FormRequestErrors.NotFound);
        var editable = request.Value.EnsureEditable();
        if (editable.IsFailure)
            return Failure(editable.Error);
        var definition = await DefinitionAsync(context.TenantId, request.Value, runtime, ct);
        if (definition is null)
            return Failure(FormRequestErrors.TemplateNotPublished);
        var consent = FormConsentDefinitionSupport
            .Read(definition)
            .SingleOrDefault(item => item.ComponentId == componentId);
        if (consent is null || !IsVisible(request.Value, definition, componentId, runtime))
            return Failure(FormRequestErrors.InvalidComponent);
        var notice = await runtime.Notices.GetByKeyAsync(context.TenantId, consent.NoticeKey, ct);
        var version = notice?.Versions.SingleOrDefault(item => item.VersionNumber == consent.NoticeVersionNumber);
        return version is null
            ? Failure(ConsentErrors.VersionNotFound)
            : Result.Success(new ResolvedFormConsent(request.Value, consent, version));
    }

    private static async Task<FormDefinition?> DefinitionAsync(
        Guid tenantId,
        FormRequest request,
        FormConsentRuntime runtime,
        CancellationToken ct
    )
    {
        var version = await runtime.Versions.GetByIdAsync(tenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return null;
        var definition = runtime.Engine.ParseDefinition(version.Value.Definition.Json);
        return definition.IsSuccess ? definition.Value : null;
    }

    private static bool IsVisible(
        FormRequest request,
        FormDefinition definition,
        string componentId,
        FormConsentRuntime runtime
    )
    {
        var raw = request.Submission?.DraftAnswers is { } draft
            ? runtime.Protector.Unprotect(draft)
            : "{\"schemaVersion\":1,\"values\":{}}";
        var answers = runtime.Engine.ParseAnswers(raw);
        return answers.IsSuccess && runtime.Engine.EvaluateVisibility(definition, answers.Value).IsVisible(componentId);
    }

    private static Result<ResolvedFormConsent> Failure(Error error) => Result.Failure<ResolvedFormConsent>(error);
}
