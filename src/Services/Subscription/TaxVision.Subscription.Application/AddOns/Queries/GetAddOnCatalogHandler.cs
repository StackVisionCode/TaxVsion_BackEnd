using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.AddOns.Queries;

public static class GetAddOnCatalogHandler
{
    public static async Task<Result<IReadOnlyList<AddOnDefinitionResponse>>> Handle(
        GetAddOnCatalogQuery query, IAddOnDefinitionRepository addOnDefinitions, CancellationToken ct)
    {
        var published = await addOnDefinitions.GetPublishedAsync(ct);

        var response = new List<AddOnDefinitionResponse>(published.Count);
        foreach (var definition in published)
        {
            response.Add(new AddOnDefinitionResponse(
                definition.Id, definition.Code.Value, definition.Name, definition.Description, definition.Category, definition.AllowMultipleInstances));
        }

        return Result.Success<IReadOnlyList<AddOnDefinitionResponse>>(response);
    }
}
