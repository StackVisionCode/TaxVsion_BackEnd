using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;

namespace TaxVision.CloudStorage.Application.Administration;

/// <summary>Cuánto ocupa una oficina; lo pide Subscription (M2M) antes de bajarle el almacenamiento.</summary>
public sealed record GetTenantStorageUsedQuery(Guid TenantId);

public sealed record TenantStorageUsedResponse(Guid TenantId, long UsedBytes, long ReservedBytes);

public static class GetTenantStorageUsedHandler
{
    public static async Task<Result<TenantStorageUsedResponse>> Handle(
        GetTenantStorageUsedQuery query,
        IStorageLimitRepository limits,
        CancellationToken ct
    )
    {
        // Sin cuota aprovisionada no pudo subir archivos: ocupa cero.
        var limit = await limits.GetAsync(query.TenantId, ct);
        return Result.Success(
            new TenantStorageUsedResponse(query.TenantId, limit?.UsedBytes ?? 0, limit?.ReservedBytes ?? 0)
        );
    }
}
