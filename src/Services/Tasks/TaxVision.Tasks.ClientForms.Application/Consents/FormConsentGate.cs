using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine.Answers;
using TaxVision.DataCollection.Engine.Components;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Visibility;
using TaxVision.Tasks.ClientForms.Application.Consents.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests;
using TaxVision.Tasks.ClientForms.Domain.Consents;
using TaxVision.Tasks.ClientForms.Domain.Requests;

namespace TaxVision.Tasks.ClientForms.Application.Consents;

public sealed class FormConsentGate(IConsentNoticeRepository notices, IFormConsentRecordRepository records)
    : IFormConsentGate
{
    private static readonly ComponentRegistry Registry = new();

    public async Task<Result> EnsureBeforeCollectionAsync(
        ParticipantContext context,
        FormRequest request,
        FormDefinition definition,
        FormAnswers? answers,
        bool alwaysProtected,
        CancellationToken ct
    )
    {
        if (!alwaysProtected && (answers is null || !ContainsProtectedData(definition, answers)))
            return Result.Success();
        var required = FormConsentDefinitionSupport
            .Read(definition)
            .Where(item => item.Blocking && item.Mode == "acceptance" && item.Timing == "beforeCollection")
            .ToArray();
        return await EnsureAcceptedAsync(context, request, required, ConsentErrors.RequiredBeforeCollection, ct);
    }

    public async Task<Result<IReadOnlyList<FormConsentRecord>>> EnsureBeforeSubmitAsync(
        ParticipantContext context,
        FormRequest request,
        FormDefinition definition,
        VisibilityMap visibility,
        CancellationToken ct
    )
    {
        var required = FormConsentDefinitionSupport
            .Read(definition)
            .Where(item => item.Blocking && item.Mode == "acceptance" && visibility.IsVisible(item.ComponentId))
            .ToArray();
        var accepted = await AcceptedAsync(context, request, required, ct);
        return accepted.Count == required.Length
            ? Result.Success<IReadOnlyList<FormConsentRecord>>(accepted)
            : Result.Failure<IReadOnlyList<FormConsentRecord>>(ConsentErrors.RequiredBeforeSubmit);
    }

    private async Task<Result> EnsureAcceptedAsync(
        ParticipantContext context,
        FormRequest request,
        IReadOnlyList<FormConsentDefinition> required,
        Error error,
        CancellationToken ct
    ) =>
        (await AcceptedAsync(context, request, required, ct)).Count == required.Count
            ? Result.Success()
            : Result.Failure(error);

    private async Task<List<FormConsentRecord>> AcceptedAsync(
        ParticipantContext context,
        FormRequest request,
        IReadOnlyList<FormConsentDefinition> required,
        CancellationToken ct
    )
    {
        var existing = await records.ListForRequestAsync(context.TenantId, request.Id, ct);
        var accepted = new List<FormConsentRecord>();
        foreach (var consent in required)
        {
            var notice = await notices.GetByKeyAsync(context.TenantId, consent.NoticeKey, ct);
            var match = notice is null
                ? null
                : existing.SingleOrDefault(item =>
                    item.ComponentId == consent.ComponentId
                    && item.ConsentNoticeId == notice.Id
                    && item.NoticeVersionNumber == consent.NoticeVersionNumber
                    && item.Outcome == FormConsentOutcome.Accepted
                    && MatchesParticipant(context, item)
                );
            if (match is not null)
                accepted.Add(match);
        }
        return accepted;
    }

    private static bool ContainsProtectedData(FormDefinition definition, FormAnswers answers)
    {
        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            if (
                section.Repeat is null
                && section.Components.Any(IsProtected)
                && section.Components.Any(component =>
                    IsProtected(component) && answers.Values.ContainsKey(component.Id)
                )
            )
                return true;
            if (
                section.Repeat is not null
                && section.Components.Any(IsProtected)
                && answers.Values.ContainsKey(section.Id)
            )
                return true;
        }
        return false;
    }

    private static bool IsProtected(FormComponent component)
    {
        if (!Registry.TryGet(component.Type, out var type) || type.Kind != ComponentKind.Input)
            return false;
        return (component.DataClass ?? type.MinimumDataClass)
            is DataClass.Financial
                or DataClass.Personal
                or DataClass.Restricted;
    }

    internal static bool MatchesParticipant(ParticipantContext context, FormConsentRecord record) =>
        context.Kind == record.ParticipantKind
        && context.CustomerId == record.CustomerId
        && context.ParticipantId == record.ParticipantId;
}
