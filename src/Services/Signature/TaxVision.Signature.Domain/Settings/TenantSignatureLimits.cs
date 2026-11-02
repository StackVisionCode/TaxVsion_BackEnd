using BuildingBlocks.Domain;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Proyección local de lo que vende el plan del tenant en firma electrónica (claves <c>signature.*</c> de
/// Subscription), alimentada por <c>TenantEntitlementsChanged</c>. Idempotente por revisión: un evento viejo
/// que llega tarde no pisa uno nuevo.
/// </summary>
public sealed class TenantSignatureLimits : TenantEntity
{
    // Lo que trae el plan cuando el snapshot no tiene la clave: los valores vigentes antes de los entitlements.
    public const int FallbackMaxDocumentsPerRequest = 20;
    public const int FallbackMaxSignersPerRequest = 50;
    public const long FallbackMaxPdfBytes = 25L * 1024 * 1024;
    public const int FallbackMaxPagesPerDocument = 100;
    public const int FallbackMaxTokenHours = 720;
    public const int FallbackMinRetentionYears = 7;
    public const bool FallbackPurgeAllowed = false;
    public const VerificationChannel FallbackChannels = VerificationChannel.Email | VerificationChannel.Sms;

    private TenantSignatureLimits() { }

    public string PlanCode { get; private set; } = string.Empty;
    public long RevisionNumber { get; private set; }
    public int MaxDocumentsPerRequest { get; private set; }
    public int MaxSignersPerRequest { get; private set; }
    public long MaxPdfBytes { get; private set; }
    public int MaxPagesPerDocument { get; private set; }
    public int MaxTokenHours { get; private set; }
    public int MinRetentionYears { get; private set; }
    public bool PurgeAllowed { get; private set; }
    public VerificationChannel AllowedChannels { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantSignatureLimits Create(Guid tenantId)
    {
        var limits = new TenantSignatureLimits { Id = Guid.NewGuid(), RevisionNumber = -1 };
        limits.SetTenant(tenantId);
        return limits;
    }

    /// <summary>Lo que rige antes de recibir el primer evento del plan.</summary>
    public static SignatureLimits Fallback() =>
        SignatureLimits.Create(
            FallbackMaxDocumentsPerRequest,
            FallbackMaxSignersPerRequest,
            FallbackMaxPdfBytes,
            FallbackMaxPagesPerDocument,
            FallbackMaxTokenHours,
            FallbackMinRetentionYears,
            FallbackPurgeAllowed,
            FallbackChannels
        );

    public void ApplyIfNewer(
        string planCode,
        long revisionNumber,
        int maxDocumentsPerRequest,
        int maxSignersPerRequest,
        long maxPdfBytes,
        int maxPagesPerDocument,
        int maxTokenHours,
        int minRetentionYears,
        bool purgeAllowed,
        VerificationChannel allowedChannels,
        DateTime nowUtc
    )
    {
        if (revisionNumber < RevisionNumber)
            return;
        PlanCode = planCode;
        RevisionNumber = revisionNumber;
        MaxDocumentsPerRequest = maxDocumentsPerRequest;
        MaxSignersPerRequest = maxSignersPerRequest;
        MaxPdfBytes = maxPdfBytes;
        MaxPagesPerDocument = maxPagesPerDocument;
        MaxTokenHours = maxTokenHours;
        MinRetentionYears = minRetentionYears;
        PurgeAllowed = purgeAllowed;
        AllowedChannels = allowedChannels;
        UpdatedAtUtc = nowUtc;
    }

    public SignatureLimits ToLimits() =>
        SignatureLimits.Create(
            MaxDocumentsPerRequest,
            MaxSignersPerRequest,
            MaxPdfBytes,
            MaxPagesPerDocument,
            MaxTokenHours,
            MinRetentionYears,
            PurgeAllowed,
            AllowedChannels
        );
}
