using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Application.Files.Queries;

public sealed record GetFileQuery(Guid TenantId, StorageActorScope Scope, Guid FileId);

public static class GetFileHandler
{
    public static async Task<Result<FileResponse>> Handle(
        GetFileQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);
        return file is null || !query.Scope.CanAccess(file)
            ? Result.Failure<FileResponse>(FileErrors.NotFound)
            : Result.Success(FileResponseMapper.Map(file));
    }
}

public sealed record ListFilesQuery(Guid TenantId, StorageActorScope Scope, int Skip, int Take);

public static class ListFilesHandler
{
    public static async Task<IReadOnlyList<FileResponse>> Handle(
        ListFilesQuery query,
        IFileObjectRepository files,
        CancellationToken ct
    ) =>
        (
            await files.ListAsync(
                query.TenantId,
                query.Scope.IsCustomerPortal ? query.Scope.CustomerId ?? Guid.Empty : null,
                Math.Max(0, query.Skip),
                Math.Clamp(query.Take, 1, 100),
                ct
            )
        )
            .Select(FileResponseMapper.Map)
            .ToArray();
}

public sealed record IssueDownloadUrlQuery(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    Guid FileId,
    RequestAuditContext Audit
);

public static class IssueDownloadUrlHandler
{
    public static async Task<Result<DownloadUrlResponse>> Handle(
        IssueDownloadUrlQuery query,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IObjectStorage storage,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var file = await files.GetAsync(query.TenantId, query.FileId, ct);
        if (file is null)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        if (!query.Scope.CanAccess(file))
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotFound);
        if (file.Status != FileStatus.Available)
            return Result.Failure<DownloadUrlResponse>(FileErrors.NotAvailable);

        var lifetime = TimeSpan.FromMinutes(Math.Clamp(options.Value.PresignedUrlMinutes, 1, 60));
        var url = await storage.PresignGetAsync(options.Value.MainBucket, file.ObjectKey, lifetime, ct);
        audit.Add(
            StorageAccessLog.Create(
                query.TenantId,
                file.Id,
                query.ActorId,
                "download.url-issued",
                "success",
                query.Audit.IpAddress,
                query.Audit.UserAgent,
                query.Audit.CorrelationId,
                null,
                clock.UtcNow
            )
        );
        await bus.PublishAsync(
            new FileAccessAuditedIntegrationEvent
            {
                TenantId = query.TenantId,
                FileId = file.Id,
                ActorId = query.ActorId,
                Action = "download.url-issued",
                Outcome = "success",
                IpAddress = query.Audit.IpAddress,
                CorrelationId = query.Audit.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);
        return Result.Success(new DownloadUrlResponse(file.Id, url, clock.UtcNow.Add(lifetime)));
    }
}
