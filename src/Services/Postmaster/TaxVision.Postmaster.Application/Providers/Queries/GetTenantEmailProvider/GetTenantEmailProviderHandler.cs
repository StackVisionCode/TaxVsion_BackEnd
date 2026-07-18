using BuildingBlocks.Results;
using TaxVision.Postmaster.Application.Providers;

namespace TaxVision.Postmaster.Application.Providers.Queries.GetTenantEmailProvider;

public static class GetTenantEmailProviderHandler
{
    public static async Task<Result<TenantEmailProviderDto>> Handle(
        GetTenantEmailProviderQuery query,
        ITenantEmailProviderRepository repository,
        CancellationToken ct
    )
    {
        var lookup = await repository.GetByCodeAsync(query.TenantId, query.ProviderCode, ct);
        if (lookup.IsFailure)
            return Result.Failure<TenantEmailProviderDto>(lookup.Error);

        var provider = lookup.Value;
        return Result.Success(
            new TenantEmailProviderDto(
                provider.Id,
                provider.ProviderCode,
                provider.DisplayName,
                provider.ProviderType.ToString(),
                provider.Host,
                provider.Port,
                provider.UseTls,
                provider.Username,
                provider.FromAddressDefault,
                provider.FromDisplayNameDefault,
                provider.RateLimitPerMinute,
                provider.Enabled,
                provider.CreatedAtUtc,
                provider.UpdatedAtUtc
            )
        );
    }
}
