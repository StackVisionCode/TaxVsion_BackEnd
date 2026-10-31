using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.CloudStorage.Domain.Files;

namespace TaxVision.CloudStorage.Domain.Quotas;

/// <summary>Alcance de un tope de tamaño: la fila más específica (Tenant) gana sobre Plan, y Plan sobre Global.</summary>
public enum UploadSizePolicyScope
{
    Global,
    Plan,
    Tenant,
}

/// <summary>
/// Tope de tamaño por archivo administrado por PlatformAdmin en BD (D-33): reemplaza el tamaño que
/// antes salía de appsettings/código, sin tocar la whitelist de tipos. No es <see cref="TenantEntity"/>
/// a propósito: las filas Global y Plan no pertenecen a ningún tenant, así que el filtro fail-closed
/// del DbContext no debe aplicarle. No confundir con TenantStoragePolicy (proveedor de almacenamiento).
/// </summary>
public sealed class UploadSizePolicy : BaseEntity
{
    /// <summary>Techo técnico absoluto (5 GB): ninguna fila puede superarlo, la subida multipart ya lo cubre.</summary>
    public const long TechnicalMaxBytes = 5L * 1024 * 1024 * 1024;

    public const int PlanCodeMaxLength = 100;

    private UploadSizePolicy() { }

    public UploadSizePolicyScope Scope { get; private set; }
    public FolderType FolderType { get; private set; }
    public string? PlanCode { get; private set; }
    public Guid? TenantId { get; private set; }
    public long MaxFileSizeBytes { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public static Result<UploadSizePolicy> CreateGlobal(
        FolderType folderType,
        long maxFileSizeBytes,
        Guid actorUserId,
        DateTime nowUtc
    ) => Create(UploadSizePolicyScope.Global, folderType, null, null, maxFileSizeBytes, actorUserId, nowUtc);

    public static Result<UploadSizePolicy> CreatePlan(
        string planCode,
        FolderType folderType,
        long maxFileSizeBytes,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var normalized = planCode?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > PlanCodeMaxLength)
            return Result.Failure<UploadSizePolicy>(UploadSizePolicyErrors.InvalidPlanCode);
        return Create(UploadSizePolicyScope.Plan, folderType, normalized, null, maxFileSizeBytes, actorUserId, nowUtc);
    }

    public static Result<UploadSizePolicy> CreateTenant(
        Guid tenantId,
        FolderType folderType,
        long maxFileSizeBytes,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (tenantId == Guid.Empty)
            return Result.Failure<UploadSizePolicy>(UploadSizePolicyErrors.InvalidTenant);
        return Create(UploadSizePolicyScope.Tenant, folderType, null, tenantId, maxFileSizeBytes, actorUserId, nowUtc);
    }

    public Result SetMaxFileSizeBytes(long bytes, Guid actorUserId, DateTime nowUtc)
    {
        if (!IsWithinRange(bytes))
            return Result.Failure(UploadSizePolicyErrors.OutOfRange);
        MaxFileSizeBytes = bytes;
        UpdatedByUserId = actorUserId;
        UpdatedAtUtc = nowUtc;
        return Result.Success();
    }

    public static bool IsWithinRange(long bytes) => bytes >= 1 && bytes <= TechnicalMaxBytes;

    private static Result<UploadSizePolicy> Create(
        UploadSizePolicyScope scope,
        FolderType folderType,
        string? planCode,
        Guid? tenantId,
        long maxFileSizeBytes,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (!Enum.IsDefined(folderType))
            return Result.Failure<UploadSizePolicy>(UploadSizePolicyErrors.InvalidFolderType);
        if (!IsWithinRange(maxFileSizeBytes))
            return Result.Failure<UploadSizePolicy>(UploadSizePolicyErrors.OutOfRange);

        return Result.Success(
            new UploadSizePolicy
            {
                Id = Guid.NewGuid(),
                Scope = scope,
                FolderType = folderType,
                PlanCode = planCode,
                TenantId = tenantId,
                MaxFileSizeBytes = maxFileSizeBytes,
                UpdatedByUserId = actorUserId,
                UpdatedAtUtc = nowUtc,
            }
        );
    }
}

/// <summary>Audit fila-por-mutación: NewMaxFileSizeBytes null = la fila se borró; OldMaxFileSizeBytes null = se creó.</summary>
public sealed class UploadSizePolicyAudit : BaseEntity
{
    private UploadSizePolicyAudit() { }

    public UploadSizePolicyScope Scope { get; private set; }
    public FolderType FolderType { get; private set; }
    public string? PlanCode { get; private set; }
    public Guid? TenantId { get; private set; }
    public long? OldMaxFileSizeBytes { get; private set; }
    public long? NewMaxFileSizeBytes { get; private set; }
    public Guid ActorUserId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static UploadSizePolicyAudit Record(
        UploadSizePolicyScope scope,
        FolderType folderType,
        string? planCode,
        Guid? tenantId,
        long? oldMaxFileSizeBytes,
        long? newMaxFileSizeBytes,
        Guid actorUserId,
        DateTime occurredAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            Scope = scope,
            FolderType = folderType,
            PlanCode = planCode,
            TenantId = tenantId,
            OldMaxFileSizeBytes = oldMaxFileSizeBytes,
            NewMaxFileSizeBytes = newMaxFileSizeBytes,
            ActorUserId = actorUserId,
            OccurredAtUtc = occurredAtUtc,
        };
}

public static class UploadSizePolicyErrors
{
    public static readonly Error OutOfRange = new(
        "UploadSizePolicy.OutOfRange",
        $"The max file size must be between 1 byte and {UploadSizePolicy.TechnicalMaxBytes} bytes (5 GB)."
    );
    public static readonly Error InvalidFolderType = new(
        "UploadSizePolicy.InvalidFolderType",
        "The folder type is not a known FolderType."
    );
    public static readonly Error InvalidPlanCode = new(
        "UploadSizePolicy.InvalidPlanCode",
        "The plan code is required and must be at most 100 characters."
    );
    public static readonly Error InvalidTenant = new("UploadSizePolicy.InvalidTenant", "The tenant id is required.");
}
