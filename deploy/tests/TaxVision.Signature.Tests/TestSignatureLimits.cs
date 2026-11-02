using TaxVision.Signature.Application.Limits;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Platform;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Tests;

/// <summary>
/// Límites para los tests que no prueban límites: los valores vigentes antes de los entitlements, con todos
/// los canales habilitados para que los tests de WhatsApp/KBA no dependan del plan.
/// </summary>
internal static class TestSignatureLimits
{
    private static readonly SignaturePlatformSettings Platform = SignaturePlatformSettings.Defaults();

    public static SignatureLimits Default { get; } =
        SignatureLimits.Create(
            TenantSignatureLimits.FallbackMaxDocumentsPerRequest,
            TenantSignatureLimits.FallbackMaxSignersPerRequest,
            TenantSignatureLimits.FallbackMaxPdfBytes,
            TenantSignatureLimits.FallbackMaxPagesPerDocument,
            TenantSignatureLimits.FallbackMaxTokenHours,
            TenantSignatureLimits.FallbackMinRetentionYears,
            TenantSignatureLimits.FallbackPurgeAllowed,
            VerificationChannel.Email
                | VerificationChannel.Sms
                | VerificationChannel.WhatsApp
                | VerificationChannel.KnowledgeBased
        );

    public static VerificationPolicy Verification => Platform.Verification;

    public static int MaxFieldsPerSigner => Platform.MaxFieldsPerSigner;

    public static int MaxRolesPerTemplate => Platform.MaxRolesPerTemplate;

    public static int MaxRemindersPerRequest => Platform.MaxRemindersPerRequest;

    public static int MaxSignatureImageBytes => Platform.MaxSignatureImageBytes;
}

/// <summary>Resolvedor fijo: devuelve la misma política para cualquier tenant.</summary>
internal sealed class FixedSignatureLimitsResolver(EffectiveSignaturePolicy? policy = null) : ISignatureLimitsResolver
{
    public EffectiveSignaturePolicy Policy { get; } =
        policy
        ?? new EffectiveSignaturePolicy(
            TestSignatureLimits.Default,
            TestSignatureLimits.Default,
            TenantSignatureSettings.DefaultTokenExpirationHours,
            RetentionPolicy.DefaultRetentionYears,
            false,
            VerificationChannel.Email
        );

    public Task<EffectiveSignaturePolicy> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Policy);
}

internal sealed class FixedSignaturePlatformSettings(SignaturePlatformSettings? settings = null)
    : ISignaturePlatformSettingsProvider
{
    public SignaturePlatformSettings Settings { get; } = settings ?? SignaturePlatformSettings.Defaults();

    public Task<SignaturePlatformSettings> GetAsync(CancellationToken ct) => Task.FromResult(Settings);

    public void Invalidate() { }
}

/// <summary>Estilo de estampado fijo: el de una oficina nueva con los valores de plataforma por defecto.</summary>
internal sealed class FixedStampStyle(TaxVision.Signature.Application.Abstractions.Sealing.StampStyle? style = null)
    : TaxVision.Signature.Application.Abstractions.Sealing.IStampStyleResolver
{
    public static TaxVision.Signature.Application.Abstractions.Sealing.StampStyle Default { get; } =
        new(
            ShowSignatureFrame: true,
            DatePattern: DateStampFormat.MonthDayYear.Pattern(),
            AutoFontMinPt: SignaturePlatformSettings.Defaults().AutoFontMinPt,
            AutoFontMaxPt: SignaturePlatformSettings.Defaults().AutoFontMaxPt
        );

    public Task<TaxVision.Signature.Application.Abstractions.Sealing.StampStyle> ResolveAsync(
        Guid tenantId,
        CancellationToken ct = default
    ) => Task.FromResult(style ?? Default);
}
