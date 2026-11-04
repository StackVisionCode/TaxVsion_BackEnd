using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Excepción que PlatformAdmin le pone a un tenant sobre lo que vende su plan (columnas <c>Plan_*</c>). Cada
/// valor nulo hereda del plan (<see cref="TenantSignatureLimits"/>); uno con valor lo reemplaza, sube o baje.
/// Sin excepción (todo nulo) rige el plan tal cual.
/// </summary>
public sealed record SignaturePlanConstraints
{
    public int? MaxDocumentsPerRequest { get; private init; }
    public int? MaxSignersPerRequest { get; private init; }
    public long? MaxAllowedPdfBytes { get; private init; }
    public int? MaxAllowedPages { get; private init; }
    public int? MinRetentionYears { get; private init; }
    public bool? PurgeAllowed { get; private init; }
    public VerificationChannel? AllowedChannels { get; private init; }
    public int? MaxTokenExpirationHours { get; private init; }

    private SignaturePlanConstraints() { }

    public static SignaturePlanConstraints None() => new();

    /// <summary>Valida cada valor dado contra los techos técnicos; los nulos no se tocan.</summary>
    public static Result<SignaturePlanConstraints> Create(
        int? maxDocumentsPerRequest,
        int? maxSignersPerRequest,
        long? maxAllowedPdfBytes,
        int? maxAllowedPages,
        int? minRetentionYears,
        bool? purgeAllowed,
        VerificationChannel? allowedChannels,
        int? maxTokenExpirationHours
    )
    {
        if (maxDocumentsPerRequest is < 1 or > SignatureLimits.AbsoluteMaxDocumentsPerRequest)
            return Invalid(
                "Documents",
                $"MaxDocumentsPerRequest must be between 1 and {SignatureLimits.AbsoluteMaxDocumentsPerRequest}."
            );
        if (maxSignersPerRequest is < 1 or > SignatureLimits.AbsoluteMaxSignersPerRequest)
            return Invalid(
                "Signers",
                $"MaxSignersPerRequest must be between 1 and {SignatureLimits.AbsoluteMaxSignersPerRequest}."
            );
        if (maxAllowedPdfBytes is < 1024 or > DocumentLimits.AbsoluteMaxPdfBytes)
            return Invalid(
                "PdfBytes",
                $"MaxAllowedPdfBytes must be between 1024 and {DocumentLimits.AbsoluteMaxPdfBytes}."
            );
        if (maxAllowedPages is < 1 or > DocumentLimits.AbsoluteMaxPages)
            return Invalid("Pages", $"MaxAllowedPages must be between 1 and {DocumentLimits.AbsoluteMaxPages}.");
        if (minRetentionYears is < RetentionPolicy.MinRetentionYears or > RetentionPolicy.MaxRetentionYears)
            return Invalid(
                "RetentionYears",
                $"MinRetentionYears must be between {RetentionPolicy.MinRetentionYears} and {RetentionPolicy.MaxRetentionYears}."
            );
        if (allowedChannels == VerificationChannel.None)
            return Invalid("Channels", "At least one verification channel must be allowed.");
        if (maxTokenExpirationHours is < SignatureLimits.MinTokenHours or > SignatureLimits.AbsoluteMaxTokenHours)
            return Invalid(
                "TokenExpiration",
                $"MaxTokenExpirationHours must be between {SignatureLimits.MinTokenHours} and {SignatureLimits.AbsoluteMaxTokenHours}."
            );

        return Result.Success(
            new SignaturePlanConstraints
            {
                MaxDocumentsPerRequest = maxDocumentsPerRequest,
                MaxSignersPerRequest = maxSignersPerRequest,
                MaxAllowedPdfBytes = maxAllowedPdfBytes,
                MaxAllowedPages = maxAllowedPages,
                MinRetentionYears = minRetentionYears,
                PurgeAllowed = purgeAllowed,
                AllowedChannels = allowedChannels,
                MaxTokenExpirationHours = maxTokenExpirationHours,
            }
        );
    }

    /// <summary>Lo que vende el plan con esta excepción encima.</summary>
    public SignatureLimits ApplyTo(SignatureLimits plan) =>
        SignatureLimits.Create(
            MaxDocumentsPerRequest ?? plan.MaxDocumentsPerRequest,
            MaxSignersPerRequest ?? plan.MaxSignersPerRequest,
            MaxAllowedPdfBytes ?? plan.MaxPdfBytes,
            MaxAllowedPages ?? plan.MaxPagesPerDocument,
            MaxTokenExpirationHours ?? plan.MaxTokenHours,
            MinRetentionYears ?? plan.MinRetentionYears,
            PurgeAllowed ?? plan.PurgeAllowed,
            AllowedChannels ?? plan.AllowedChannels
        );

    private static Result<SignaturePlanConstraints> Invalid(string field, string message) =>
        Result.Failure<SignaturePlanConstraints>(new Error($"Signature.PlanConstraints.{field}", message));
}
