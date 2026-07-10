using BuildingBlocks.Results;

namespace TaxVision.Signature.Domain.Settings;

/// <summary>
/// Techos que la PLATAFORMA impone sobre la configuración que un tenant admin puede
/// elegir. Separar "lo que el plan contratado permite" de "lo que el tenant eligió
/// dentro de ese margen" es lo que impide que el preparador tenga libertad irrestricta.
///
/// Valores por defecto = plan básico (Email + SMS, 25 MB, 7 años, sin purge).
/// El endpoint de plataforma PUT /admin/tenants/{id}/signature-constraints los eleva
/// según el plan contratado (Starter, Pro, Enterprise, etc.).
/// </summary>
public sealed record SignaturePlanConstraints
{
    // Defaults plan básico
    public const long DefaultMaxAllowedPdfBytes   = 25L * 1024 * 1024;   // 25 MB
    public const long DefaultMaxAllowedImageBytes = 10L * 1024 * 1024;   // 10 MB
    public const int  DefaultMaxAllowedPages      = 100;
    public const int  DefaultMinRetentionYears    = 7;    // mínimo IRS para docs fiscales
    public const bool DefaultPurgeAllowed         = false;
    public const VerificationChannel DefaultAllowedChannels = VerificationChannel.Email | VerificationChannel.Sms;
    public const int  DefaultMaxTokenExpirationHours        = 720; // 30 días

    /// <summary>Tamaño máximo de PDF que el tenant puede configurar (bytes).</summary>
    public long MaxAllowedPdfBytes { get; private init; }

    /// <summary>Tamaño máximo de imagen que el tenant puede configurar (bytes).</summary>
    public long MaxAllowedImageBytes { get; private init; }

    /// <summary>Máximo de páginas por documento que el tenant puede configurar.</summary>
    public int MaxAllowedPages { get; private init; }

    /// <summary>El tenant no puede configurar retención menor a este valor.</summary>
    public int MinRetentionYears { get; private init; }

    /// <summary>Si false, el tenant no puede activar AllowPurge en su RetentionPolicy.</summary>
    public bool PurgeAllowed { get; private init; }

    /// <summary>
    /// Bitmask de canales que la plataforma habilitó para este tenant/plan.
    /// El tenant sólo puede elegir dentro de esta máscara.
    /// </summary>
    public VerificationChannel AllowedChannels { get; private init; }

    /// <summary>El tenant no puede configurar tokenExpiration mayor a este valor.</summary>
    public int MaxTokenExpirationHours { get; private init; }

    private SignaturePlanConstraints() { }

    public static SignaturePlanConstraints Default() => new()
    {
        MaxAllowedPdfBytes      = DefaultMaxAllowedPdfBytes,
        MaxAllowedImageBytes    = DefaultMaxAllowedImageBytes,
        MaxAllowedPages         = DefaultMaxAllowedPages,
        MinRetentionYears       = DefaultMinRetentionYears,
        PurgeAllowed            = DefaultPurgeAllowed,
        AllowedChannels         = DefaultAllowedChannels,
        MaxTokenExpirationHours = DefaultMaxTokenExpirationHours,
    };

    /// <summary>
    /// Construye restricciones validadas desde valores externos (endpoint de plataforma).
    /// Aplica los techos absolutos del dominio Signature para que una configuración de
    /// plataforma nunca supere lo que el propio sistema puede manejar.
    /// </summary>
    public static Result<SignaturePlanConstraints> Create(
        long maxAllowedPdfBytes,
        long maxAllowedImageBytes,
        int maxAllowedPages,
        int minRetentionYears,
        bool purgeAllowed,
        VerificationChannel allowedChannels,
        int maxTokenExpirationHours
    )
    {
        if (maxAllowedPdfBytes is <= 0 or > DocumentLimits.AbsoluteMaxPdfBytes)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.PdfBytes",
                $"MaxAllowedPdfBytes must be between 1 and {DocumentLimits.AbsoluteMaxPdfBytes}."));

        if (maxAllowedImageBytes is <= 0 or > DocumentLimits.AbsoluteMaxPdfBytes)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.ImageBytes",
                $"MaxAllowedImageBytes must be between 1 and {DocumentLimits.AbsoluteMaxPdfBytes}."));

        if (maxAllowedPages is <= 0 or > DocumentLimits.AbsoluteMaxPages)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.Pages",
                $"MaxAllowedPages must be between 1 and {DocumentLimits.AbsoluteMaxPages}."));

        if (minRetentionYears is < RetentionPolicy.MinRetentionYears or > RetentionPolicy.MaxRetentionYears)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.RetentionYears",
                $"MinRetentionYears must be between {RetentionPolicy.MinRetentionYears} and {RetentionPolicy.MaxRetentionYears}."));

        if (allowedChannels == VerificationChannel.None)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.Channels",
                "At least one verification channel must be allowed."));

        if (maxTokenExpirationHours is < TenantSignatureSettings.MinTokenExpirationHours
            or > TenantSignatureSettings.MaxTokenExpirationHours)
            return Result.Failure<SignaturePlanConstraints>(new Error(
                "Signature.PlanConstraints.TokenExpiration",
                $"MaxTokenExpirationHours must be between {TenantSignatureSettings.MinTokenExpirationHours} " +
                $"and {TenantSignatureSettings.MaxTokenExpirationHours}."));

        return Result.Success(new SignaturePlanConstraints
        {
            MaxAllowedPdfBytes      = maxAllowedPdfBytes,
            MaxAllowedImageBytes    = maxAllowedImageBytes,
            MaxAllowedPages         = maxAllowedPages,
            MinRetentionYears       = minRetentionYears,
            PurgeAllowed            = purgeAllowed,
            AllowedChannels         = allowedChannels,
            MaxTokenExpirationHours = maxTokenExpirationHours,
        });
    }
}
