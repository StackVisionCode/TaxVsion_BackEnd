using System.Text.RegularExpressions;
using BuildingBlocks.Results;
using TaxVision.Tasks.ClientForms.Application.Mapping.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Mapping.Responses;

namespace TaxVision.Tasks.ClientForms.Application.Mapping.Queries;

public sealed record GetFormCatalogUsageQuery(string? TargetKey);

public static partial class GetFormCatalogUsageHandler
{
    public static async Task<Result<FormCatalogUsageResponse>> Handle(
        GetFormCatalogUsageQuery query,
        IFormCatalogUsageReader usage,
        CancellationToken ct
    )
    {
        if (query.TargetKey is null || query.TargetKey.Length > 128 || !TargetKeyPattern().IsMatch(query.TargetKey))
            return Result.Failure<FormCatalogUsageResponse>(FormCatalogUsageErrors.InvalidTargetKey);
        var counts = await usage.CountAsync(query.TargetKey, DateTime.UtcNow, ct);
        return Result.Success(
            new FormCatalogUsageResponse(
                query.TargetKey,
                counts.OpenRequests,
                counts.ActiveIntakeLinks,
                counts.PublishedVersions
            )
        );
    }

    [GeneratedRegex(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex TargetKeyPattern();
}
