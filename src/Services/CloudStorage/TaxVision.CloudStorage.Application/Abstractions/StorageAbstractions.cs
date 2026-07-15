using TaxVision.CloudStorage.Domain.Audit;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Folders;
using TaxVision.CloudStorage.Domain.Legal;
using TaxVision.CloudStorage.Domain.Quotas;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Application.Abstractions;

public interface IFileObjectRepository
{
    void Add(FileObject file);
    void Remove(FileObject file);
    Task<FileObject?> GetAsync(Guid tenantId, Guid fileId, CancellationToken ct);
    Task<IReadOnlyList<FileObject>> ListAsync(
        Guid tenantId,
        Guid? restrictedCustomerId,
        int skip,
        int take,
        CancellationToken ct
    );
    Task<IReadOnlyList<FileObject>> ListExpiredUploadsAsync(DateTime nowUtc, int take, CancellationToken ct);

    /// <summary>Papelera de un tenant (Fase C1) — todo lo que esta en FileStatus.SoftDeleted, sin filtrar por vencimiento.</summary>
    Task<IReadOnlyList<FileObject>> ListSoftDeletedAsync(Guid tenantId, int skip, int take, CancellationToken ct);

    /// <summary>Candidatos a purga definitiva (Fase C1, cross-tenant): SoftDeleted cuyo SoftDeleteExpiresAtUtc ya paso.</summary>
    Task<IReadOnlyList<FileObject>> ListPurgeablePastRetentionAsync(DateTime nowUtc, int take, CancellationToken ct);

    /// <summary>Fase C2 — archivos directamente dentro de una carpeta navegable (null = raiz), sin los borrados de la papelera.</summary>
    Task<IReadOnlyList<FileObject>> ListInFolderAsync(
        Guid tenantId,
        Guid? folderId,
        Guid? restrictedCustomerId,
        CancellationToken ct
    );
}

/// <summary>Fase C2 — carpetas navegables (arbol logico, ver Domain/Folders/Folder.cs).</summary>
public interface IFolderRepository
{
    void Add(Folder folder);
    Task<Folder?> GetAsync(Guid tenantId, Guid folderId, CancellationToken ct);

    /// <summary>Subcarpetas directas de parentFolderId (null = raiz del owner).</summary>
    Task<IReadOnlyList<Folder>> ListSubfoldersAsync(
        Guid tenantId,
        Guid? parentFolderId,
        Guid? restrictedCustomerId,
        CancellationToken ct
    );

    /// <summary>
    /// Todo el subarbol bajo relativePathPrefix (sin incluir la carpeta duena de ese
    /// path), via prefijo de RelativePath — usado para cascadear rename/move y para
    /// detectar ciclos. Recibe el path como string (no la Folder) para que el
    /// llamador pueda pasar el path ANTERIOR a una mutacion sin que un Rename/
    /// Reparent ya aplicado en memoria lo pise antes de consultar.
    /// </summary>
    Task<IReadOnlyList<Folder>> ListByPathPrefixAsync(Guid tenantId, string relativePathPrefix, CancellationToken ct);

    Task<bool> NameExistsUnderParentAsync(
        Guid tenantId,
        Guid? parentFolderId,
        string name,
        Guid? excludeFolderId,
        CancellationToken ct
    );
}

public interface IStorageLimitRepository
{
    void Add(TenantStorageLimit limit);
    Task<TenantStorageLimit?> GetAsync(Guid tenantId, CancellationToken ct);
}

/// <summary>Fase C3 — links de compartir (nucleo publico/privado, ver Domain/Sharing/ShareLink.cs).</summary>
public interface IShareLinkRepository
{
    void Add(ShareLink link);

    /// <summary>Gestion autenticada (revocar, cambiar expiracion, listar) — siempre filtrado por tenant.</summary>
    Task<ShareLink?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>
    /// Resolucion de un token entrante (publico o privado): el token no codifica
    /// TenantId, asi que esta busqueda es deliberadamente cross-tenant — el
    /// aislamiento lo aplica el handler despues, comparando link.TenantId contra
    /// el tenant del JWT (privado) o la regla de Visibility (publico).
    /// </summary>
    Task<ShareLink?> GetByTokenHashAsync(byte[] tokenHash, CancellationToken ct);

    Task<IReadOnlyList<ShareLink>> ListForResourceAsync(
        Guid tenantId,
        Guid resourceId,
        ShareResourceType resourceType,
        CancellationToken ct
    );

    Task<IReadOnlyList<ShareLink>> ListSharedWithUserAsync(
        Guid tenantId,
        Guid userId,
        int skip,
        int take,
        CancellationToken ct
    );

    Task<IReadOnlyList<ShareLink>> ListSharedWithCustomerAsync(
        Guid tenantId,
        Guid customerId,
        int skip,
        int take,
        CancellationToken ct
    );

    /// <summary>
    /// Fase C4 — links Visibility.Public activos de tipo Folder cuyo ResourceId
    /// esta en folderIds (la carpeta destino + toda su cadena de ancestros) —
    /// usado por MoveFileToFolderHandler para decidir si alertar. El llamador
    /// filtra por IsRecursive segun si el link es la carpeta directa o un
    /// ancestro (ver MoveFileToFolderHandler.AlertActivePublicFolderShares).
    /// </summary>
    Task<IReadOnlyList<ShareLink>> ListActivePublicFolderSharesAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> folderIds,
        CancellationToken ct
    );
}

/// <summary>Fase C3 — hash de la contrasena opcional de un link publico. Mismo esquema PBKDF2 que Auth.IPasswordHasher.</summary>
public interface IShareLinkPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface IStorageAuditRepository
{
    void Add(StorageAccessLog log);
    Task<IReadOnlyList<StorageAccessLog>> ListAsync(Guid tenantId, int skip, int take, CancellationToken ct);
}

/// <summary>Fase L1.3 — expedientes DMCA (ver Domain/Legal/DmcaNotice.cs).</summary>
public interface IDmcaNoticeRepository
{
    void Add(DmcaNotice notice);
    Task<DmcaNotice?> GetAsync(Guid tenantId, Guid id, CancellationToken ct);

    /// <summary>Evita registrar un segundo takedown activo (Received/CounterNoticeSubmitted) contra el mismo archivo mientras el primero sigue abierto.</summary>
    Task<bool> HasActiveNoticeForFileAsync(Guid tenantId, Guid fileId, CancellationToken ct);
}

public interface IObjectStorage
{
    Task<PresignedUpload> CreateUploadPolicyAsync(
        string bucket,
        string objectKey,
        string contentType,
        long exactSizeBytes,
        TimeSpan lifetime,
        CancellationToken ct
    );
    Task<Uri> PresignGetAsync(string bucket, string objectKey, TimeSpan lifetime, CancellationToken ct);
    Task<long> GetSizeAsync(string bucket, string objectKey, CancellationToken ct);
    Task<bool> ExistsAsync(string bucket, string objectKey, CancellationToken ct);
    Task DownloadAsync(string bucket, string objectKey, Stream destination, CancellationToken ct);
    Task CopyAsync(string sourceBucket, string objectKey, string destinationBucket, CancellationToken ct);

    /// <summary>Fase D0 — copia entre buckets renombrando la key (el objeto de origen vive en la key propia del servicio llamador, no en la key canonica de CloudStorage).</summary>
    Task CopyAsync(
        string sourceBucket,
        string sourceObjectKey,
        string destinationBucket,
        string destinationObjectKey,
        CancellationToken ct
    );
    Task DeleteAsync(string bucket, string objectKey, CancellationToken ct);
}

/// <summary>
/// Fase U — presigned multipart upload (initiate/complete/abort). Puerto separado
/// de <see cref="IObjectStorage"/> a proposito: usa un SDK distinto (AWSSDK.S3,
/// no el SDK "Minio" que respalda IObjectStorage) porque el SDK "Minio" no expone
/// publicamente los primitivos de multipart presign (verificado — son `internal`
/// en la libreria). Mismo servidor MinIO por debajo, cliente REST distinto.
/// </summary>
public interface IMultipartUploadStorage
{
    Task<MultipartUploadInitiation> InitiateAsync(
        string bucket,
        string objectKey,
        string contentType,
        long totalSizeBytes,
        long partSizeBytes,
        TimeSpan urlLifetime,
        CancellationToken ct
    );

    Task CompleteAsync(
        string bucket,
        string objectKey,
        string uploadId,
        IReadOnlyList<MultipartPart> parts,
        CancellationToken ct
    );

    /// <summary>Libera las partes ya subidas de un upload que nunca se completo (expiro o el cliente lo abandono).</summary>
    Task AbortAsync(string bucket, string objectKey, string uploadId, CancellationToken ct);
}

public sealed record MultipartUploadInitiation(string UploadId, IReadOnlyList<MultipartPartUploadUrl> Parts);

public sealed record MultipartPartUploadUrl(int PartNumber, Uri UploadUrl);

/// <summary>ETag que MinIO devuelve al subir cada parte — el cliente lo manda de vuelta en el complete.</summary>
public sealed record MultipartPart(int PartNumber, string ETag);

public sealed record PresignedUpload(Uri Url, IReadOnlyDictionary<string, string> FormData);

public interface IVirusScanner
{
    Task<VirusScanResult> ScanAsync(Stream content, CancellationToken ct);
}

public sealed record VirusScanResult(VirusScanVerdict Verdict, string Report)
{
    public static VirusScanResult Clean(string report = "Clean") => new(VirusScanVerdict.Clean, report);

    public static VirusScanResult Infected(string report) => new(VirusScanVerdict.Infected, report);

    public static VirusScanResult Error(string report) => new(VirusScanVerdict.Error, report);
}

public enum VirusScanVerdict
{
    Clean,
    Infected,
    Error,
}

public interface IFileContentInspector
{
    Task<InspectedContent> InspectAsync(Stream content, string originalName, CancellationToken ct);
}

/// <summary>
/// Escaneo de CONTENIDO (no virus — eso es <see cref="IVirusScanner"/>): NSFW,
/// CSAM, u otra politica de moderacion. MVP registra <c>NoOpContentScanner</c>
/// (siempre Clean) via DI — el pipeline en <c>ScanFileHandler</c> ya maneja los
/// 3 verdicts reales para que enchufar un scanner de verdad despues sea swap de
/// implementacion, sin tocar el flujo. Corre SIEMPRE (no hay flag por tenant):
/// mismo criterio que <see cref="IVirusScanner"/>, que tampoco es opcional.
/// </summary>
public interface IContentScanner
{
    Task<ContentScanResult> ScanAsync(Stream content, ContentScanContext context, CancellationToken ct);
}

public sealed record ContentScanContext(Guid TenantId, Guid FileId, OwnerType OwnerType, string OriginalName);

public sealed record ContentScanResult(ContentScanVerdict Verdict, string? Reason)
{
    public static ContentScanResult Clean() => new(ContentScanVerdict.Clean, null);

    public static ContentScanResult PolicyViolation(string reason) => new(ContentScanVerdict.PolicyViolation, reason);

    public static ContentScanResult Uncertain(string reason) => new(ContentScanVerdict.Uncertain, reason);
}

public enum ContentScanVerdict
{
    Clean,
    PolicyViolation,
    Uncertain,
}

public sealed record InspectedContent(
    string ContentType,
    string Sha256,
    bool IsSafe = true,
    string? RejectionReason = null
);

public interface IObjectKeyBuilder
{
    BuildingBlocks.Results.Result<ObjectKey> Build(
        Guid fileId,
        Guid tenantId,
        OwnerType ownerType,
        Guid? ownerId,
        FolderType folderType,
        int? taxYear,
        string originalName
    );
}

public interface ISystemClock
{
    DateTime UtcNow { get; }
}

public sealed record RequestAuditContext(string? IpAddress, string? UserAgent, string CorrelationId);

public sealed record StorageActorScope(bool IsCustomerPortal, Guid? CustomerId)
{
    public bool CanCreate(OwnerType ownerType, Guid? ownerId) =>
        !IsCustomerPortal || (CustomerId.HasValue && ownerType == OwnerType.Customer && ownerId == CustomerId);

    public bool CanAccess(FileObject file) => CanCreate(file.OwnerType, file.OwnerId);

    /// <summary>Fase C2 — misma regla de aislamiento que los archivos, aplicada a carpetas.</summary>
    public bool CanAccess(Folder folder) => CanCreate(folder.OwnerType, folder.OwnerId);
}
