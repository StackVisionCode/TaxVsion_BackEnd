using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.CloudStorage.Domain.Sharing;

/// <summary>
/// Agregado que gobierna politica/estado/auditoria de un link de compartir (largo
/// plazo, revocable). El binario nunca se sirve directo desde aca — cada acceso
/// resuelto (ver ResolvePublicShareHandler/ResolvePrivateShareHandler) emite una
/// presigned URL de MinIO efimera (60-300s), fresca en cada request.
/// </summary>
public sealed class ShareLink : TenantEntity
{
    public const int DefaultLifetimeDays = 7;

    private readonly List<ShareRecipient> _recipients = [];

    private ShareLink() { }

    public Guid ResourceId { get; private set; }
    public ShareResourceType ResourceType { get; private set; }
    public byte[] TokenHash { get; private set; } = default!;
    public string TokenLast4 { get; private set; } = default!;
    public ShareVisibility Visibility { get; private set; }
    public SharePermission Permission { get; private set; }
    public string? PasswordHash { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }
    public int? MaxAccessCount { get; private set; }
    public int AccessCount { get; private set; }

    /// <summary>Fase C4 — si el link cubre todo el subarbol (true) o solo el contenido directo de la carpeta (false). Siempre false en un link de File.</summary>
    public bool IsRecursive { get; private set; }

    /// <summary>Fase C4 — si el link cubre automaticamente lo agregado despues de crearlo (true) o solo lo que ya existia (false). Siempre false en un link de File.</summary>
    public bool AppliesToFutureItems { get; private set; }
    public ShareStatus Status { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public IReadOnlyList<ShareRecipient> Recipients => _recipients;

    /// <summary>
    /// Token de concurrencia optimista (SQL Server rowversion). Sin esto, dos accesos
    /// concurrentes al mismo link publico pueden leer el mismo AccessCount base y
    /// ambos incrementar+guardar sin verse — dejando AccessCount por debajo de lo real
    /// y permitiendo superar MaxAccessCount. Con IsRowVersion(), el segundo SaveChanges
    /// en conflicto tira DbUpdateConcurrencyException en vez de pisar al primero
    /// (mismo patron que TenantStorageLimit.RowVersion).
    /// </summary>
    public byte[] RowVersion { get; private set; } = [];

    public static Result<(ShareLink Link, string PlainToken)> Create(
        Guid id,
        Guid tenantId,
        Guid resourceId,
        ShareResourceType resourceType,
        ShareVisibility visibility,
        SharePermission permission,
        string? passwordHash,
        DateTime? expiresAtUtc,
        int? maxAccessCount,
        Guid createdBy,
        DateTime nowUtc,
        bool isRecursive = false,
        bool appliesToFutureItems = false
    )
    {
        if (resourceType == ShareResourceType.File && (isRecursive || appliesToFutureItems))
            return Result.Failure<(ShareLink, string)>(ShareErrors.RecursiveOnlyForFolders);

        if (maxAccessCount is <= 0)
            return Result.Failure<(ShareLink, string)>(ShareErrors.InvalidMaxAccessCount);

        var expiry = expiresAtUtc ?? nowUtc.AddDays(DefaultLifetimeDays);
        if (expiry <= nowUtc)
            return Result.Failure<(ShareLink, string)>(ShareErrors.ExpirationInPast);

        var token = ShareToken.Create();
        var link = new ShareLink
        {
            Id = id,
            ResourceId = resourceId,
            ResourceType = resourceType,
            TokenHash = token.Hash,
            TokenLast4 = token.Last4,
            Visibility = visibility,
            Permission = permission,
            PasswordHash = passwordHash,
            ExpiresAtUtc = expiry,
            MaxAccessCount = maxAccessCount,
            AccessCount = 0,
            IsRecursive = isRecursive,
            AppliesToFutureItems = appliesToFutureItems,
            Status = ShareStatus.Active,
            CreatedByUserId = createdBy,
            CreatedAtUtc = nowUtc,
        };
        link.SetTenant(tenantId);
        return Result.Success((link, token.Value));
    }

    public bool IsExpired(DateTime nowUtc) => ExpiresAtUtc <= nowUtc;

    public bool IsUsable(DateTime nowUtc) => Status == ShareStatus.Active && !IsExpired(nowUtc);

    /// <summary>Vista combinada para listados/API — nunca usar Status solo, siempre pasar por aca.</summary>
    public ShareLinkEffectiveStatus EffectiveStatus(DateTime nowUtc)
    {
        if (Status == ShareStatus.Revoked)
            return ShareLinkEffectiveStatus.Revoked;
        if (Status == ShareStatus.Exhausted)
            return ShareLinkEffectiveStatus.Exhausted;
        if (IsExpired(nowUtc))
            return ShareLinkEffectiveStatus.Expired;
        return ShareLinkEffectiveStatus.Active;
    }

    /// <summary>
    /// Incrementa el contador de acceso. La atomicidad bajo concurrencia la da
    /// RowVersion (optimistic concurrency), no este metodo en si mismo — dos llamadas
    /// concurrentes sobre la misma fila hacen que una de las dos transacciones falle
    /// en SaveChanges en vez de perderse silenciosamente.
    /// </summary>
    public void RegisterAccess(DateTime nowUtc)
    {
        AccessCount++;
        if (MaxAccessCount is { } max && AccessCount >= max)
            Status = ShareStatus.Exhausted;
    }

    public Result Revoke(DateTime nowUtc)
    {
        if (Status == ShareStatus.Revoked)
            return Result.Failure(ShareErrors.AlreadyRevoked);
        Status = ShareStatus.Revoked;
        RevokedAtUtc = nowUtc;
        return Result.Success();
    }

    public Result UpdateExpiration(DateTime newExpiresAtUtc, DateTime nowUtc)
    {
        if (newExpiresAtUtc <= nowUtc)
            return Result.Failure(ShareErrors.ExpirationInPast);
        ExpiresAtUtc = newExpiresAtUtc;
        return Result.Success();
    }

    public void AddUserRecipient(Guid userId) => _recipients.Add(ShareRecipient.ForUser(Id, userId));

    public void AddCustomerRecipient(Guid customerId) => _recipients.Add(ShareRecipient.ForCustomer(Id, customerId));

    public void AddExternalRecipient(string email) => _recipients.Add(ShareRecipient.ForEmail(Id, email));

    public bool HasUserRecipient(Guid userId) =>
        _recipients.Exists(r => r.Kind == ShareRecipientKind.User && r.RecipientUserId == userId);

    public bool HasCustomerRecipient(Guid customerId) =>
        _recipients.Exists(r => r.Kind == ShareRecipientKind.Customer && r.RecipientCustomerId == customerId);

    public bool HasEmailRecipient(string email) =>
        _recipients.Exists(r =>
            r.Kind == ShareRecipientKind.Email
            && string.Equals(r.RecipientEmail, email.Trim(), StringComparison.OrdinalIgnoreCase)
        );

    public bool HasAnyRecipient => _recipients.Count > 0;
}
