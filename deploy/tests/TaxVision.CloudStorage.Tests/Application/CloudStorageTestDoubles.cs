using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;
using Wolverine;
using Wolverine.Runtime;
using Wolverine.Transports.Sending;

namespace TaxVision.CloudStorage.Tests.Application;

/// <summary>Fakes compartidos por los tests de comandos de aplicacion (papelera Fase C1, carpetas Fase C2).</summary>
internal sealed class FakeFileObjectRepository : IFileObjectRepository
{
    private readonly Dictionary<Guid, FileObject> _byId = [];

    public void Seed(FileObject file) => _byId[file.Id] = file;

    public bool Removed(Guid fileId) => !_byId.ContainsKey(fileId);

    public void Add(FileObject file) => _byId[file.Id] = file;

    public void Remove(FileObject file) => _byId.Remove(file.Id);

    public Task<FileObject?> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(fileId, out var file) && file.TenantId == tenantId ? file : null);

    public Task<IReadOnlyList<FileObject>> ListAsync(
        Guid tenantId,
        Guid? restrictedCustomerId,
        int skip,
        int take,
        CancellationToken ct
    ) => Task.FromResult<IReadOnlyList<FileObject>>([]);

    public Task<IReadOnlyList<FileObject>> ListExpiredUploadsAsync(DateTime nowUtc, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<FileObject>>([]);

    public Task<IReadOnlyList<FileObject>> ListSoftDeletedAsync(
        Guid tenantId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<FileObject>>(
            _byId.Values.Where(file => file.TenantId == tenantId && file.Status == FileStatus.SoftDeleted).ToList()
        );

    public Task<IReadOnlyList<FileObject>> ListPurgeablePastRetentionAsync(
        DateTime nowUtc,
        int take,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<FileObject>>(
            _byId
                .Values.Where(file => file.Status == FileStatus.SoftDeleted && file.SoftDeleteExpiresAtUtc <= nowUtc)
                .ToList()
        );

    public Task<IReadOnlyList<FileObject>> ListInFolderAsync(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<FileObject>>(
            _byId
                .Values.Where(file =>
                    file.TenantId == tenantId && file.FolderId == folderId && file.Status != FileStatus.SoftDeleted
                )
                .ToList()
        );

    public Task<IReadOnlyList<FileObject>> ListInFoldersAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        Guid? restrictedCustomerId,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<FileObject>>(
            _byId
                .Values.Where(file =>
                    file.TenantId == tenantId
                    && file.FolderId is { } folderId
                    && folderIds.Contains(folderId)
                    && file.Status != FileStatus.SoftDeleted
                )
                .ToList()
        );
}

internal sealed class FakeFolderRepository : IFolderRepository
{
    private readonly Dictionary<Guid, Folder> _byId = [];

    public void Seed(Folder folder) => _byId[folder.Id] = folder;

    public void Add(Folder folder) => _byId[folder.Id] = folder;

    public Task<Folder?> GetAsync(Guid tenantId, Guid folderId, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(folderId, out var folder) && folder.TenantId == tenantId ? folder : null);

    public Task<IReadOnlyList<Folder>> ListSubfoldersAsync(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<Folder>>(
            _byId.Values.Where(f => f.TenantId == tenantId && f.ParentFolderId == parentFolderId).ToList()
        );

    public Task<IReadOnlyList<Folder>> ListByPathPrefixAsync(
        Guid tenantId,
        string relativePathPrefix,
        CancellationToken ct
    )
    {
        var prefix = relativePathPrefix + "/";
        return Task.FromResult<IReadOnlyList<Folder>>(
            _byId.Values.Where(f => f.TenantId == tenantId && f.RelativePath.StartsWith(prefix)).ToList()
        );
    }

    public Task<bool> NameExistsUnderParentAsync(
        Guid tenantId,
        Guid? parentFolderId,
        string name,
        Guid? excludeFolderId,
        CancellationToken ct
    ) =>
        Task.FromResult(
            _byId.Values.Any(f =>
                f.TenantId == tenantId
                && f.ParentFolderId == parentFolderId
                && f.Name == name
                && f.Id != (excludeFolderId ?? Guid.Empty)
            )
        );
}

internal sealed class FakeStorageLimitRepository : IStorageLimitRepository
{
    private readonly Dictionary<Guid, TenantStorageLimit> _byTenant = [];

    public void Seed(TenantStorageLimit limit) => _byTenant[limit.TenantId] = limit;

    public void Add(TenantStorageLimit limit) => _byTenant[limit.TenantId] = limit;

    public Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct) =>
        Task.FromResult(_byTenant.GetValueOrDefault(tenantId));
}

/// <summary>Fase C3 — resuelve por hash igual que la implementacion real (byte[] no tiene equality estructural, se compara con SequenceEqual).</summary>
internal sealed class FakeShareLinkRepository : IShareLinkRepository
{
    private readonly Dictionary<Guid, ShareLink> _byId = [];

    public void Seed(ShareLink link) => _byId[link.Id] = link;

    public void Add(ShareLink link) => _byId[link.Id] = link;

    public Task<ShareLink?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(id, out var link) && link.TenantId == tenantId ? link : null);

    public Task<ShareLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct) =>
        Task.FromResult(_byId.Values.FirstOrDefault(link => link.TokenHash.SequenceEqual(tokenHash)));

    public Task<IReadOnlyList<ShareLink>> ListForResourceAsync(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType resourceType,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<ShareLink>>(
            _byId
                .Values.Where(link =>
                    link.TenantId == tenantId && link.ResourceId == resourceId && link.ResourceType == resourceType
                )
                .ToList()
        );

    public Task<IReadOnlyList<ShareLink>> ListSharedWithUserAsync(
        Guid tenantId,
        Guid userId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<ShareLink>>(
            _byId
                .Values.Where(link =>
                    link.TenantId == tenantId
                    && link.Status == ShareStatus.Active
                    && (link.Visibility == ShareVisibility.TenantOnly || link.HasUserRecipient(userId))
                )
                .ToList()
        );

    public Task<IReadOnlyList<ShareLink>> ListSharedWithCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<ShareLink>>(
            _byId
                .Values.Where(link =>
                    link.TenantId == tenantId
                    && link.Status == ShareStatus.Active
                    && link.Visibility == ShareVisibility.TenantCustomers
                    && (!link.HasAnyRecipient || link.HasCustomerRecipient(customerId))
                )
                .ToList()
        );

    public Task<IReadOnlyList<ShareLink>> ListActivePublicFolderSharesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<ShareLink>>(
            _byId
                .Values.Where(link =>
                    link.TenantId == tenantId
                    && link.Status == ShareStatus.Active
                    && link.ResourceType == ShareResourceType.Folder
                    && link.Visibility == ShareVisibility.Public
                    && folderIds.Contains(link.ResourceId)
                )
                .ToList()
        );
}

/// <summary>Fase C3 — hash reversible trivial (no PBKDF2) para que los tests no paguen el costo de derivacion real.</summary>
internal sealed class FakeShareLinkPasswordHasher : IShareLinkPasswordHasher
{
    public string Hash(string password) => $"hashed:{password}";

    public bool Verify(string password, string hash) => hash == $"hashed:{password}";
}

internal sealed class FakeStorageAuditRepository : IStorageAuditRepository
{
    public List<StorageAccessLog> Logs { get; } = [];

    public void Add(StorageAccessLog log) => Logs.Add(log);

    public Task<IReadOnlyList<StorageAccessLog>> ListAsync(Guid tenantId, int skip, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<StorageAccessLog>>(Logs);
}

internal sealed class FakeDmcaNoticeRepository : IDmcaNoticeRepository
{
    private readonly Dictionary<Guid, DmcaNotice> _byId = [];

    public void Seed(DmcaNotice notice) => _byId[notice.Id] = notice;

    public void Add(DmcaNotice notice) => _byId[notice.Id] = notice;

    public Task<DmcaNotice?> GetAsync(Guid tenantId, Guid id, CancellationToken ct) =>
        Task.FromResult(_byId.TryGetValue(id, out var notice) && notice.TenantId == tenantId ? notice : null);

    public Task<bool> HasActiveNoticeForFileAsync(Guid tenantId, Guid fileId, CancellationToken ct) =>
        Task.FromResult(
            _byId.Values.Any(notice =>
                notice.TenantId == tenantId
                && notice.FileId == fileId
                && notice.Status is DmcaNoticeStatus.Received or DmcaNoticeStatus.CounterNoticeSubmitted
            )
        );
}

internal sealed class FakeObjectStorage : IObjectStorage
{
    public List<(string Bucket, string ObjectKey)> Deleted { get; } = [];

    public Task<PresignedUpload> CreateUploadPolicyAsync(
        string bucket,
        string objectKey,
        string contentType,
        long exactSizeBytes,
        TimeSpan lifetime,
        CancellationToken ct
    ) => throw new NotImplementedException();

    public Task<Uri> PresignGetAsync(string bucket, string objectKey, TimeSpan lifetime, CancellationToken ct) =>
        Task.FromResult(new Uri($"https://minio.local/{bucket}/{objectKey}"));

    public List<(string Bucket, string ObjectKey, string ContentDisposition)> PresignedWithDisposition { get; } = [];

    public Task<Uri> PresignGetAsync(
        string bucket,
        string objectKey,
        TimeSpan lifetime,
        string contentDisposition,
        CancellationToken ct
    )
    {
        PresignedWithDisposition.Add((bucket, objectKey, contentDisposition));
        return Task.FromResult(
            new Uri($"https://minio.local/{bucket}/{objectKey}?disposition={Uri.EscapeDataString(contentDisposition)}")
        );
    }

    /// <summary>Null (el default) = comportamiento previo, revienta si algun test la llama sin haberla seteado antes.</summary>
    public long? SizeToReturn { get; set; }

    public Task<long> GetSizeAsync(string bucket, string objectKey, CancellationToken ct) =>
        SizeToReturn is { } size ? Task.FromResult(size) : throw new NotImplementedException();

    /// <summary>Seedable para probar el guard de idempotencia de SaveFileFromSourceHandler (destino ya copiado en un intento anterior).</summary>
    public HashSet<(string Bucket, string ObjectKey)> Existing { get; } = [];

    public Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken ct) =>
        Task.FromResult(
            Existing.Contains((bucket, objectKey))
                || Copied.Any(c => c.DestinationBucket == bucket && c.DestinationObjectKey == objectKey)
        );

    public Task DownloadAsync(string bucket, string objectKey, Stream destination, CancellationToken ct) =>
        throw new NotImplementedException();

    public Task CopyAsync(string sourceBucket, string objectKey, string destinationBucket, CancellationToken ct) =>
        throw new NotImplementedException();

    public List<(
        string SourceBucket,
        string SourceObjectKey,
        string DestinationBucket,
        string DestinationObjectKey
    )> Copied { get; } = [];

    public Task CopyAsync(
        string sourceBucket,
        string sourceObjectKey,
        string destinationBucket,
        string destinationObjectKey,
        CancellationToken ct
    )
    {
        Copied.Add((sourceBucket, sourceObjectKey, destinationBucket, destinationObjectKey));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string bucket, string objectKey, CancellationToken ct)
    {
        Deleted.Add((bucket, objectKey));
        return Task.CompletedTask;
    }
}

/// <summary>Fase U — fake de IMultipartUploadStorage (AWSSDK.S3 en Infrastructure, no el SDK "Minio").</summary>
internal sealed class FakeMultipartUploadStorage : IMultipartUploadStorage
{
    public List<(string Bucket, string ObjectKey, long TotalSizeBytes, long PartSizeBytes)> Initiated { get; } = [];
    public List<(
        string Bucket,
        string ObjectKey,
        string UploadId,
        IReadOnlyList<MultipartPart> Parts
    )> Completed { get; } = [];
    public List<(string Bucket, string ObjectKey, string UploadId)> Aborted { get; } = [];

    /// <summary>Simula un fallo de ensamblado en el storage (ETags invalidos, parte faltante, etc).</summary>
    public bool ThrowOnComplete { get; set; }

    public Task<MultipartUploadInitiation> InitiateAsync(
        string bucket,
        string objectKey,
        string contentType,
        long totalSizeBytes,
        long partSizeBytes,
        TimeSpan urlLifetime,
        CancellationToken ct
    )
    {
        Initiated.Add((bucket, objectKey, totalSizeBytes, partSizeBytes));
        var uploadId = $"upload-{Guid.NewGuid():N}";
        var partCount = (int)Math.Ceiling((double)totalSizeBytes / partSizeBytes);
        var parts = Enumerable
            .Range(1, partCount)
            .Select(n => new MultipartPartUploadUrl(n, new Uri($"https://minio.local/{bucket}/{objectKey}?part={n}")))
            .ToArray();
        return Task.FromResult(new MultipartUploadInitiation(uploadId, parts));
    }

    public Task CompleteAsync(
        string bucket,
        string objectKey,
        string uploadId,
        IReadOnlyList<MultipartPart> parts,
        CancellationToken ct
    )
    {
        if (ThrowOnComplete)
            throw new InvalidOperationException("Simulated multipart complete failure.");
        Completed.Add((bucket, objectKey, uploadId, parts));
        return Task.CompletedTask;
    }

    public Task AbortAsync(string bucket, string objectKey, string uploadId, CancellationToken ct)
    {
        Aborted.Add((bucket, objectKey, uploadId));
        return Task.CompletedTask;
    }
}

internal sealed class FakeSystemClock(DateTime utcNow) : ISystemClock
{
    public DateTime UtcNow { get; } = utcNow;
}

internal sealed class FakeUnitOfWork : BuildingBlocks.Persistence.IUnitOfWork
{
    public int SaveChangesCallCount { get; private set; }

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        SaveChangesCallCount++;
        return Task.FromResult(1);
    }
}

/// <summary>Fake minimo de IMessageBus — solo captura lo publicado via PublishAsync; el resto lanza si se usa.</summary>
internal sealed class FakeMessageBus : IMessageBus
{
    public List<object> Published { get; } = [];

    public ValueTask PublishAsync<T>(T message, DeliveryOptions? options = null)
    {
        if (message is not null)
            Published.Add(message);
        return ValueTask.CompletedTask;
    }

    public ValueTask SendAsync<T>(T message, DeliveryOptions? options = null) => throw new NotImplementedException();

    public ValueTask BroadcastToTopicAsync(string topicName, object message, DeliveryOptions? options = null) =>
        throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message) => throw new NotImplementedException();

    public IReadOnlyList<Envelope> PreviewSubscriptions(object message, DeliveryOptions options) =>
        throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(string endpointName) => throw new NotImplementedException();

    public IDestinationEndpoint EndpointFor(Uri uri) => throw new NotImplementedException();

    public Task InvokeForTenantAsync(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeForTenantAsync<T>(
        string tenantId,
        object message,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public string? TenantId
    {
        get => null;
        set { }
    }

    public Task InvokeAsync(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task InvokeAsync(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(object message, CancellationToken cancellation = default, TimeSpan? timeout = null) =>
        throw new NotImplementedException();

    public Task<T> InvokeAsync<T>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default,
        TimeSpan? timeout = null
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();

    public IAsyncEnumerable<TResponse> StreamAsync<TResponse>(
        object message,
        DeliveryOptions options,
        CancellationToken cancellation = default
    ) => throw new NotImplementedException();
}
