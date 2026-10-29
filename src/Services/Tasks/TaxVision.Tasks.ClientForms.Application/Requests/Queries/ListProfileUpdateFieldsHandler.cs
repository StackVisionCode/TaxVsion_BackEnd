using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Definitions;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Actions;
using TaxVision.Tasks.ClientForms.Application.Requests.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Commands;
using TaxVision.Tasks.ClientForms.Application.Templates.Abstractions;

namespace TaxVision.Tasks.ClientForms.Application.Requests.Queries;

public static class ListProfileUpdateFieldsHandler
{
    public static async Task<Result<ProfileUpdateFieldsResponse>> Handle(
        ListProfileUpdateFieldsQuery query,
        IFormRequestRepository requests,
        IFormRequestVisibilityFilter visibility,
        IFormTemplateVersionRepository versions,
        IFormEngine engine,
        IDataCollectionTargetCatalog catalog,
        CancellationToken ct
    )
    {
        var found = await ReviewSupport.LoadVisible(
            query.TenantId,
            query.ActorUserId,
            query.CanViewAll,
            query.RequestId,
            requests,
            visibility,
            ct
        );
        if (found.IsFailure)
            return Result.Failure<ProfileUpdateFieldsResponse>(found.Error);

        var request = found.Value;
        var outcomes = MapOutcomes(request);
        var version = await versions.GetByIdAsync(query.TenantId, request.FormTemplateVersionId, ct);
        if (version.IsFailure)
            return Result.Success(new ProfileUpdateFieldsResponse(request.ProfileUpdateStatus, [], outcomes));
        var rules = engine.ParseRuleSets(version.Value.MappingSetJson, version.Value.ActionSetJson);
        var definition = engine.ParseDefinition(version.Value.Definition.Json);
        if (rules.IsFailure || definition.IsFailure)
            return Result.Success(new ProfileUpdateFieldsResponse(request.ProfileUpdateStatus, [], outcomes));
        var fields = BuildFields(rules.Value, definition.Value, catalog.GetSnapshot());
        return Result.Success(new ProfileUpdateFieldsResponse(request.ProfileUpdateStatus, fields, outcomes));
    }

    private static FormProfileUpdateOutcomeResponse[] MapOutcomes(
        TaxVision.Tasks.ClientForms.Domain.Requests.FormRequest request
    ) =>
        request
            .ProfileUpdateOutcomes.Select(outcome => new FormProfileUpdateOutcomeResponse(
                outcome.Target,
                outcome.Applied,
                outcome.FailureCode
            ))
            .ToArray();

    private static List<ProfileUpdateFieldResponse> BuildFields(
        FormRuleSets rules,
        FormDefinition definition,
        MappingCatalogSnapshot snapshot
    )
    {
        var labels = BuildLabels(definition);
        var fields = new List<ProfileUpdateFieldResponse>();
        foreach (var mapping in rules.Mappings)
        {
            var target = snapshot.FindTarget(mapping.TargetKey);
            if (
                target is null
                || target.Status == MappingTargetStatus.Retired
                || !target.Operations.Contains(FormActionNames.UpdateCustomerProfile, StringComparer.Ordinal)
            )
                continue;
            var isTaxId = target.DataType == MappingTargetType.TaxId;
            fields.Add(
                new ProfileUpdateFieldResponse(
                    mapping.ComponentId,
                    labels.GetValueOrDefault(mapping.ComponentId, mapping.ComponentId),
                    target.TargetKey,
                    target.DisplayName,
                    target.DataType.ToString(),
                    target.RequiresTenantAdmin || isTaxId,
                    IsTaxId: isTaxId
                )
            );
        }
        return fields;
    }

    private static Dictionary<string, string> BuildLabels(FormDefinition definition)
    {
        var labels = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var section in definition.Pages.SelectMany(page => page.Sections))
        {
            if (section.Repeat is not null)
                labels[section.Id] = section.Title ?? section.Id;
            foreach (var component in section.Components)
                labels[component.Id] = component.Label ?? component.Key ?? component.Id;
        }
        return labels;
    }
}
