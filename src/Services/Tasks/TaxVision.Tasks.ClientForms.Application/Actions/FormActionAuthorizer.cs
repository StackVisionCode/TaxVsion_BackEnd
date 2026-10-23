using BuildingBlocks.Results;
using TaxVision.DataCollection.Engine;
using TaxVision.DataCollection.Engine.Mapping;
using TaxVision.Tasks.ClientForms.Application.Actions.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Participants.Abstractions;
using TaxVision.Tasks.ClientForms.Domain.Requests;
using TaxVision.Tasks.ClientForms.Domain.Versions;

namespace TaxVision.Tasks.ClientForms.Application.Actions;

public sealed class FormActionAuthorizer(
    IFormEngine engine,
    IDataCollectionTargetCatalog catalog,
    IFormResponsibleUserValidator responsibleUsers
) : IFormActionAuthorizer
{
    public async Task<Result> EnsureResponsibleCanRunAsync(
        Guid tenantId,
        Guid responsibleUserId,
        FormTemplateVersion version,
        CancellationToken ct = default
    )
    {
        var required = RequiredPermissions(version);
        if (required is null)
            return Result.Failure(FormRequestErrors.ResponsibleNotAuthorized);
        if (required.Count == 0)
            return Result.Success();
        return await responsibleUsers.HasPermissionsAsync(tenantId, responsibleUserId, required, ct)
            ? Result.Success()
            : Result.Failure(FormRequestErrors.ResponsibleNotAuthorized);
    }

    // Null si las acciones fijadas no se pueden leer: sin saber qué exigen, no se autoriza.
    private IReadOnlyCollection<string>? RequiredPermissions(FormTemplateVersion version)
    {
        var rules = engine.ParseRuleSets(version.MappingSetJson, version.ActionSetJson);
        if (rules.IsFailure)
            return null;
        var snapshot = catalog.GetSnapshot();
        var required = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in rules.Value.Actions)
        {
            var operation = snapshot.FindOperation(action.Type.ToString());
            if (operation is not null)
                required.UnionWith(operation.RequiredPermissions);
        }
        return required;
    }
}
