namespace BuildingBlocks.RateLimiting;

public static partial class RateLimitPolicyCatalog
{
    public static readonly RateLimitPolicyDefinition CloudStorageFileGet = Define(
        "cloudstorage.f.file_get",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CloudStorageFileList = Define(
        "cloudstorage.f.file_list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CloudStorageDownloadUrl = Define(
        "cloudstorage.f.download_url",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Contents + Tree (FoldersController) — mismo perfil de navegación de carpetas.
    public static readonly RateLimitPolicyDefinition CloudStorageFolderBrowse = Define(
        "cloudstorage.f.folder_browse",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por ListForFile + ListForFolder + SharedWithMe (ShareLinksController) — todas
    // lecturas de metadata de share links, mismo perfil de costo.
    public static readonly RateLimitPolicyDefinition CloudStorageShareRead = Define(
        "cloudstorage.f.share_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CloudStoragePrivateShareResolve = Define(
        "cloudstorage.f.private_share_resolve",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por GetUsage + GetAudit (StorageAdministrationController) — ambos dashboards de
    // lectura paginada, mismo perfil de costo.
    public static readonly RateLimitPolicyDefinition CloudStorageAdminRead = Define(
        "cloudstorage.f.admin_read",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    public static readonly RateLimitPolicyDefinition CloudStorageRecycleBinList = Define(
        "cloudstorage.f.recycle_bin_list",
        RateLimitCategory.F,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 300,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 3000
    );

    // Compartida por Delete + MoveToFolder + SetLegalHold + LiftLegalHold (FilesController) — todas
    // escrituras simples sobre un archivo existente.
    public static readonly RateLimitPolicyDefinition CloudStorageFileManage = Define(
        "cloudstorage.g.file_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Create + Rename + Move + Delete (FoldersController).
    public static readonly RateLimitPolicyDefinition CloudStorageFolderManage = Define(
        "cloudstorage.g.folder_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por Create + CreateForFolder + Revoke + UpdateExpiration + ChangePermission
    // (ShareLinksController) — todas escrituras simples sobre el ciclo de vida de un share link.
    public static readonly RateLimitPolicyDefinition CloudStorageShareManage = Define(
        "cloudstorage.g.share_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por RegisterTakedown + SubmitCounterNotice + Reinstate (LegalController) — flujo
    // DMCA completo, baja frecuencia por diseño.
    public static readonly RateLimitPolicyDefinition CloudStorageLegalManage = Define(
        "cloudstorage.g.legal_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition CloudStorageSettingsManage = Define(
        "cloudstorage.g.settings_manage",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    public static readonly RateLimitPolicyDefinition CloudStorageRecycleBinRestore = Define(
        "cloudstorage.g.recycle_bin_restore",
        RateLimitCategory.G,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 60,
        windowSeconds: 60,
        RateLimitAlgorithm.TokenBucket,
        overlayQuota: 600
    );

    // Compartida por InitiateUpload + CompleteUpload + InitiateMultipartUpload +
    // CompleteMultipartUpload (FilesController) — todo el ciclo de vida de un upload.
    public static readonly RateLimitPolicyDefinition CloudStorageUpload = Define(
        "cloudstorage.i.upload",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 25,
        windowSeconds: 600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 100
    );

    // Reemplaza 1:1 [EnableRateLimiting("zip-download")] — mismo cupo exacto (5/min) que ya
    // estaba tuneado para este endpoint real, ahora particionado por User (antes era
    // sub-claim-o-IP crudo de ASP.NET Core) en vez de Tenant|User — evita que dos usuarios
    // distintos del mismo tenant compartan cupo de un endpoint que ya sabía ser costoso
    // (streaming de ZIP completo).
    public static readonly RateLimitPolicyDefinition CloudStorageZipDownload = Define(
        "cloudstorage.i.zip_download",
        RateLimitCategory.I,
        RateLimitPartitionDimension.User,
        [],
        quota: 5,
        windowSeconds: 60,
        RateLimitAlgorithm.FixedWindow
    );

    public static readonly RateLimitPolicyDefinition CloudStorageRecycleBinEmpty = Define(
        "cloudstorage.i.recycle_bin_empty",
        RateLimitCategory.I,
        RateLimitPartitionDimension.Tenant | RateLimitPartitionDimension.User,
        [RateLimitPartitionDimension.Tenant],
        quota: 10,
        windowSeconds: 3600,
        RateLimitAlgorithm.FixedWindow,
        overlayQuota: 40
    );
}
