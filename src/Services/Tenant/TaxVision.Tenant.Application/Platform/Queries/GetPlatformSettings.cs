using BuildingBlocks.Results;
using TaxVision.Tenant.Application.Platform.Abstractions;

namespace TaxVision.Tenant.Application.Platform.Queries;

public sealed record GetPlatformSettingsQuery;

public static class GetPlatformSettingsHandler
{
    public static async Task<Result<PlatformSettingsResponse>> Handle(
        GetPlatformSettingsQuery _,
        IPlatformSettingsRepository repository,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(ct);
        return settings is null
            ? Result.Failure<PlatformSettingsResponse>(PlatformSettingsMapping.NotFound)
            : Result.Success(settings.ToResponse());
    }
}
