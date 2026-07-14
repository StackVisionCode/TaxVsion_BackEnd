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

/// <summary>Fase B2 — un archivo ya resuelto y listo para escribirse como entry del ZIP.</summary>
public sealed record ZipDownloadEntry(Guid FileId, string ObjectKey, string EntryName, long SizeBytes);

public sealed record ZipDownloadPlan(IReadOnlyList<ZipDownloadEntry> Entries);

public sealed record PrepareZipDownloadQuery(
    Guid TenantId,
    Guid ActorId,
    StorageActorScope Scope,
    IReadOnlyList<Guid> FileIds,
    RequestAuditContext Audit
);

/// <summary>
/// Fase B2 — valida y arma el plan de una descarga ZIP (multi-archivo), pero NO
/// escribe los bytes: eso lo hace el controller via IObjectStorage.DownloadAsync
/// directo al Response.Body (streaming), fuera del bus de Wolverine a proposito
/// (un handler no tiene acceso a HttpResponse). Este handler solo decide QUE va
/// en el ZIP y audita la decision, igual que IssueDownloadUrlHandler audita la
/// emision de una URL presignada sin bajar el archivo el mismo.
/// </summary>
public static class PrepareZipDownloadHandler
{
    public static async Task<Result<ZipDownloadPlan>> Handle(
        PrepareZipDownloadQuery query,
        IFileObjectRepository files,
        IStorageAuditRepository audit,
        IOptions<CloudStorageOptions> options,
        ISystemClock clock,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var requestedIds = query.FileIds.Distinct().ToArray();
        if (requestedIds.Length == 0)
            return Result.Failure<ZipDownloadPlan>(FileErrors.NoFilesRequested);

        var config = options.Value;
        if (requestedIds.Length > config.MaxZipFiles)
            return Result.Failure<ZipDownloadPlan>(FileErrors.TooManyItems);

        var resolved = new List<FileObject>(requestedIds.Length);
        foreach (var fileId in requestedIds)
        {
            var file = await files.GetAsync(query.TenantId, fileId, ct);
            if (file is null || !query.Scope.CanAccess(file))
                return Result.Failure<ZipDownloadPlan>(FileErrors.NotFound);
            if (file.Status != FileStatus.Available)
                return Result.Failure<ZipDownloadPlan>(FileErrors.NotAvailable);
            resolved.Add(file);
        }

        var totalBytes = resolved.Sum(file => file.SizeBytes);
        if (totalBytes > config.MaxZipAggregateBytes)
            return Result.Failure<ZipDownloadPlan>(FileErrors.ZipTooLarge);

        var entries = BuildEntries(resolved);

        audit.Add(
            StorageAccessLog.Create(
                query.TenantId,
                null,
                query.ActorId,
                "download.zip",
                "success",
                query.Audit.IpAddress,
                query.Audit.UserAgent,
                query.Audit.CorrelationId,
                $"files={resolved.Count};bytes={totalBytes}",
                clock.UtcNow
            )
        );
        foreach (var file in resolved)
        {
            await bus.PublishAsync(
                new FileAccessAuditedIntegrationEvent
                {
                    TenantId = query.TenantId,
                    FileId = file.Id,
                    ActorId = query.ActorId,
                    Action = "download.zip",
                    Outcome = "success",
                    IpAddress = query.Audit.IpAddress,
                    CorrelationId = query.Audit.CorrelationId,
                }
            );
        }
        await unitOfWork.SaveChangesAsync(ct);

        return Result.Success(new ZipDownloadPlan(entries));
    }

    /// <summary>Desambigua nombres repetidos con sufijo _1, _2... antes de la extension, en el orden pedido.</summary>
    private static List<ZipDownloadEntry> BuildEntries(IReadOnlyList<FileObject> files)
    {
        var seenCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var entries = new List<ZipDownloadEntry>(files.Count);
        foreach (var file in files)
        {
            var name = file.OriginalName;
            var occurrence = seenCounts.TryGetValue(name, out var count) ? count : 0;
            seenCounts[name] = occurrence + 1;

            var entryName =
                occurrence == 0
                    ? name
                    : $"{Path.GetFileNameWithoutExtension(name)}_{occurrence}{Path.GetExtension(name)}";
            entries.Add(new ZipDownloadEntry(file.Id, file.ObjectKey, entryName, file.SizeBytes));
        }
        return entries;
    }
}
