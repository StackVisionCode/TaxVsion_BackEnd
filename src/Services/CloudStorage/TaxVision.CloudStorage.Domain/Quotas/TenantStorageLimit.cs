using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Sharing;

namespace TaxVision.CloudStorage.Domain.Quotas;

public sealed class TenantStorageLimit : TenantEntity
{
    private TenantStorageLimit() { }

    public string PlanCode { get; private set; } = "starter";
    public long MaxBytes { get; private set; }
    public long UsedBytes { get; private set; }
    public long ReservedBytes { get; private set; }
    /// <summary>Tamaño por archivo que vende el plan (<c>storage.max_file_bytes</c>).</summary>
    public long MaxFileSizeBytes { get; private set; }

    /// <summary>Grupos de tipos de archivo que vende el plan (<c>storage.file_type_groups</c>), separados por coma.</summary>
    public string PlanFileTypeGroups { get; private set; } = PlanFileAllowanceDefaults.FileTypeGroups;

    /// <summary>La oficina restringe lo que suben sus clientes desde el portal: grupos (null = los del plan).</summary>
    public string? PortalFileTypeGroups { get; private set; }

    /// <summary>La oficina restringe el tamaño de lo que suben sus clientes (null = el de la carpeta/plan).</summary>
    public long? PortalMaxFileBytes { get; private set; }

    public bool IsSuspended { get; private set; }

    /// <summary>Destino activo para archivos del tenant. CRM mantiene MinIO como destino predeterminado.</summary>
    public string StorageProvider { get; private set; } = "internal";

    /// <summary>Habilitado por defecto; un Tenant Admin lo puede desactivar explicitamente desde Settings.</summary>
    public bool AllowPublicShareLinks { get; private set; } = true;

    /// <summary>Link externo sin email (ShareVisibility.ExternalLink). Habilitado por default; la oficina lo puede apagar.</summary>
    public bool AllowLinkOnlyExternalShares { get; private set; } = true;

    /// <summary>Si la oficina exige contraseña en los links externos sin email.</summary>
    public bool RequirePasswordOnLinkShares { get; private set; }

    /// <summary>Tope de vida (días) de un enlace externo (público, solo-link o a destinatarios externos).</summary>
    public int MaxShareLifetimeDays { get; private set; } = 30;

    /// <summary>Vida (días) de un enlace cuando quien lo crea no elige fecha.</summary>
    public int DefaultShareLifetimeDays { get; private set; } = 7;

    /// <summary>Largo mínimo de la contraseña de un enlace (cuando se pone una).</summary>
    public int MinSharePasswordLength { get; private set; } = 8;

    /// <summary>Intentos de contraseña fallidos antes de bloquear el enlace un rato (anti fuerza bruta).</summary>
    public int MaxSharePasswordAttempts { get; private set; } = DefaultMaxSharePasswordAttempts;

    public const int DefaultMaxSharePasswordAttempts = 10;

    /// <summary>Tope de descargas de un enlace externo (null = sin tope); también es el valor si no se elige uno.</summary>
    public int? MaxShareDownloads { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static TenantStorageLimit Create(
        Guid tenantId,
        string planCode,
        long maxBytes,
        long maxFileSizeBytes,
        string? fileTypeGroups = null
    )
    {
        var limit = new TenantStorageLimit
        {
            Id = tenantId,
            PlanCode = planCode,
            MaxBytes = maxBytes,
            MaxFileSizeBytes = maxFileSizeBytes,
            PlanFileTypeGroups = fileTypeGroups ?? PlanFileAllowanceDefaults.FileTypeGroups,
            AllowPublicShareLinks = true,
            AllowLinkOnlyExternalShares = true,
            MaxShareLifetimeDays = 30,
        };
        limit.SetTenant(tenantId);
        return limit;
    }

    /// <summary>
    /// Solo cuota del tenant. El tope por archivo no se chequea acá: lo resuelve IUploadPolicyResolver
    /// (carpeta, plan y excepciones en BD) antes de reservar.
    /// </summary>
    public Result Reserve(long bytes)
    {
        if (IsSuspended)
            return Result.Failure(QuotaErrors.Suspended);
        if (bytes <= 0)
            return Result.Failure(QuotaErrors.FileTooLarge);
        if (UsedBytes + ReservedBytes + bytes > MaxBytes)
            return Result.Failure(QuotaErrors.Exceeded);
        ReservedBytes += bytes;
        return Result.Success();
    }

    public void Commit(long bytes)
    {
        ReservedBytes = Math.Max(0, ReservedBytes - bytes);
        UsedBytes += bytes;
    }

    public void Release(long bytes) => ReservedBytes = Math.Max(0, ReservedBytes - bytes);

    /// <summary>
    /// Libera cuota comprometida (Fase C1): solo se llama al purgar definitivamente
    /// un archivo (borrado permanente de MinIO + registro), nunca al soft-delete —
    /// mientras el archivo esta en la papelera el objeto fisico sigue ocupando
    /// espacio real y por eso sigue contando contra UsedBytes.
    /// </summary>
    public void ReleaseUsed(long bytes) => UsedBytes = Math.Max(0, UsedBytes - bytes);

    public void ApplyPlan(string planCode, long maxBytes, long maxFileSizeBytes, string? fileTypeGroups = null)
    {
        PlanCode = planCode;
        MaxBytes = maxBytes;
        MaxFileSizeBytes = maxFileSizeBytes;
        PlanFileTypeGroups = fileTypeGroups ?? PlanFileAllowanceDefaults.FileTypeGroups;
        IsSuspended = false;
    }

    public PlanFileAllowance PlanAllowance() =>
        new(PlanFileAllowanceDefaults.ParseGroups(PlanFileTypeGroups), MaxFileSizeBytes);

    public PortalUploadRestriction PortalRestriction() =>
        new(PortalFileTypeGroups is null ? null : PlanFileAllowanceDefaults.ParseGroups(PortalFileTypeGroups), PortalMaxFileBytes);

    /// <summary>
    /// La oficina acota lo que suben sus clientes. Solo restringe: los grupos deben estar entre los del plan
    /// (los demás no podrían subirse igual) y el tamaño, si se da, es de al menos 1 MB.
    /// </summary>
    public Result SetPortalUploadRestriction(IReadOnlyCollection<string>? fileTypeGroups, long? maxFileBytes)
    {
        if (maxFileBytes is < MinPortalMaxFileBytes)
            return Result.Failure(QuotaErrors.InvalidPortalUploadRestriction);

        if (fileTypeGroups is null)
        {
            PortalFileTypeGroups = null;
        }
        else
        {
            var planGroups = PlanFileAllowanceDefaults.ParseGroups(PlanFileTypeGroups);
            var groups = fileTypeGroups.Select(group => group.Trim().ToLowerInvariant()).Distinct().ToList();
            if (groups.Count == 0 || groups.Any(group => !planGroups.Contains(group)))
                return Result.Failure(QuotaErrors.InvalidPortalUploadRestriction);
            PortalFileTypeGroups = string.Join(',', groups);
        }

        PortalMaxFileBytes = maxFileBytes;
        return Result.Success();
    }

    public const long MinPortalMaxFileBytes = 1L * 1024 * 1024;

    public void Suspend() => IsSuspended = true;

    public void EnablePublicSharing() => AllowPublicShareLinks = true;

    public void DisablePublicSharing() => AllowPublicShareLinks = false;

    /// <summary>Solo se permite activar proveedores externos tras registrar una conexión válida.</summary>
    public void SetStorageProvider(string provider) => StorageProvider = provider;

    public const int MaxShareLifetimeCeilingDays = 3650;

    /// <summary>
    /// Reglas de compartir de la oficina. Vida por defecto dentro de la máxima; contraseña de 4 a 64; de 3 a 50
    /// intentos antes de bloquear. Aplican a los enlaces externos (público, solo-link, destinatarios externos).
    /// </summary>
    public Result SetSharingPolicy(
        bool allowLinkOnlyExternalShares,
        bool requirePasswordOnLinkShares,
        int defaultLifetimeDays,
        int maxLifetimeDays,
        int minPasswordLength,
        int maxPasswordAttempts,
        int? maxDownloads
    )
    {
        if (maxLifetimeDays is < 1 or > MaxShareLifetimeCeilingDays || defaultLifetimeDays < 1 || defaultLifetimeDays > maxLifetimeDays)
            return Result.Failure(QuotaErrors.InvalidSharingPolicy);
        if (minPasswordLength is < 4 or > 64 || maxPasswordAttempts is < 3 or > 50 || maxDownloads is < 1 or > 100_000)
            return Result.Failure(QuotaErrors.InvalidSharingPolicy);

        AllowLinkOnlyExternalShares = allowLinkOnlyExternalShares;
        RequirePasswordOnLinkShares = requirePasswordOnLinkShares;
        DefaultShareLifetimeDays = defaultLifetimeDays;
        MaxShareLifetimeDays = maxLifetimeDays;
        MinSharePasswordLength = minPasswordLength;
        MaxSharePasswordAttempts = maxPasswordAttempts;
        MaxShareDownloads = maxDownloads;
        return Result.Success();
    }

    /// <summary>Contraseña puesta en cualquier enlace: largo mínimo de la oficina.</summary>
    public Result CheckSharePassword(string? password) =>
        !string.IsNullOrWhiteSpace(password) && password.Length < MinSharePasswordLength
            ? Result.Failure(ShareErrors.PasswordTooShort(MinSharePasswordLength))
            : Result.Success();

    /// <summary>
    /// Reglas de la oficina para un enlace externo nuevo: contraseña obligatoria en los enlaces sin
    /// autenticación (si la oficina la exige), vida máxima y tope de descargas.
    /// </summary>
    public Result CheckNewExternalShare(ShareVisibility visibility, string? password, DateTime? expiresAtUtc, int? maxAccessCount, DateTime nowUtc)
    {
        if (RequirePasswordOnLinkShares && visibility.IsUnauthenticatedLink() && string.IsNullOrWhiteSpace(password))
            return Result.Failure(ShareErrors.PasswordRequiredForLinkShare);
        if (MaxShareDownloads is { } cap && maxAccessCount > cap)
            return Result.Failure(ShareErrors.DownloadsExceedMax(cap));
        return CheckExternalExpiration(expiresAtUtc, nowUtc);
    }

    /// <summary>Al crear o extender un enlace externo, la fecha no puede pasar de la vida máxima de la oficina.</summary>
    public Result CheckExternalExpiration(DateTime? expiresAtUtc, DateTime nowUtc) =>
        expiresAtUtc is { } expiry && expiry > nowUtc.AddDays(MaxShareLifetimeDays)
            ? Result.Failure(ShareErrors.ShareLifetimeExceedsMax)
            : Result.Success();
}

public static class QuotaErrors
{
    public static readonly Error NotProvisioned = new(
        "StorageQuota.NotProvisioned",
        "Storage limits have not been provisioned for this tenant."
    );
    public static readonly Error Suspended = new("StorageQuota.Suspended", "Storage is suspended for this tenant.");
    public static readonly Error FileTooLarge = new(
        "StorageQuota.FileTooLarge",
        "The file exceeds the plan's per-file limit."
    );
    public static readonly Error Exceeded = new("StorageQuota.Exceeded", "The tenant storage quota would be exceeded.");
    public static readonly Error InvalidSharingPolicy = new(
        "StorageQuota.InvalidSharingPolicy",
        "Link lifetime must be 1–3650 days (default not above the maximum), passwords 4–64 characters, 3–50 attempts and downloads 1–100000."
    );
    public static readonly Error InvalidPortalUploadRestriction = new(
        "StorageQuota.InvalidPortalUploadRestriction",
        "Client uploads can only be limited to file types your plan includes, and to at least 1 MB."
    );
}
