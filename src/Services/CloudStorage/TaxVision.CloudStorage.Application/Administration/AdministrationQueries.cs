using BuildingBlocks.Results;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Administration;

public sealed record StorageUsageResponse(
    string PlanCode,
    long UsedBytes,
    long ReservedBytes,
    long MaxBytes,
    long AvailableBytes,
    long MaxFileSizeBytes,
    bool IsSuspended,
    bool AllowPublicShareLinks
);

public sealed record GetStorageUsageQuery(Guid TenantId);

public static class GetStorageUsageHandler
{
    public static async Task<Result<StorageUsageResponse>> Handle(
        GetStorageUsageQuery query,
        IStorageLimitRepository limits,
        CancellationToken ct
    )
    {
        var limit = await limits.GetAsync(query.TenantId, ct);
        if (limit is null)
            return Result.Failure<StorageUsageResponse>(QuotaErrors.NotProvisioned);
        return Result.Success(
            new StorageUsageResponse(
                limit.PlanCode,
                limit.UsedBytes,
                limit.ReservedBytes,
                limit.MaxBytes,
                Math.Max(0, limit.MaxBytes - limit.UsedBytes - limit.ReservedBytes),
                limit.MaxFileSizeBytes,
                limit.IsSuspended,
                limit.AllowPublicShareLinks
            )
        );
    }
}

public sealed record AuditEntryResponse(
    Guid Id,
    Guid? FileId,
    Guid ActorId,
    string Action,
    string Outcome,
    string? IpAddress,
    string CorrelationId,
    string? Details,
    DateTime OccurredAtUtc
);

public sealed record ListStorageAuditQuery(Guid TenantId, int Skip, int Take);

public static class ListStorageAuditHandler
{
    public static async Task<IReadOnlyList<AuditEntryResponse>> Handle(
        ListStorageAuditQuery query,
        IStorageAuditRepository audit,
        CancellationToken ct
    ) =>
        (await audit.ListAsync(query.TenantId, Math.Max(0, query.Skip), Math.Clamp(query.Take, 1, 100), ct))
            .Select(entry => new AuditEntryResponse(
                entry.Id,
                entry.FileId,
                entry.ActorId,
                entry.Action,
                entry.Outcome,
                entry.IpAddress,
                entry.CorrelationId,
                entry.Details,
                entry.OccurredAtUtc
            ))
            .ToArray();
}
