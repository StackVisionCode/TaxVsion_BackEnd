using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Requests.ValueObjects;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Limits;

/// <summary>Límites vigentes del tenant para los frontends (el wizard y los ajustes no tienen números propios).</summary>
public sealed record GetEffectiveSignatureLimitsQuery(Guid TenantId);

/// <param name="Plan">Techos del plan con la excepción de PlatformAdmin: hasta dónde puede llegar la oficina.</param>
/// <param name="MaxPdfBytes">Tamaño de PDF vigente (lo de la oficina dentro del techo).</param>
/// <param name="MaxPagesPerDocument">Páginas vigentes (lo de la oficina dentro del techo).</param>
/// <param name="DefaultTokenHours">Vida por defecto del enlace del firmante.</param>
public sealed record EffectiveSignatureLimitsResponse(
    SignatureCeilingsResponse Plan,
    long MaxPdfBytes,
    int MaxPagesPerDocument,
    int MinTokenHours,
    int DefaultTokenHours,
    int RetentionYears,
    int MaxRetentionYears,
    bool PurgeEnabled,
    IReadOnlyList<string> AllowedChannels,
    string DefaultChannel,
    int OtpLength,
    int OtpLifetimeMinutes,
    int OtpResendCooldownSeconds,
    int MaxSignatureImageBytes,
    int MaxProfilesPerPreparer,
    int MaxRolesPerTemplate,
    int MaxFieldsPerSigner,
    int MinReminderIntervalHours,
    int MaxReminderIntervalHours,
    int MinDeliveryLinkDays,
    int MaxDeliveryLinkDays,
    long MinDeliveryAttachmentBytes,
    long MaxDeliveryAttachmentBytes,
    bool ShowSignatureFrame,
    string DateStampFormat,
    string DatePattern,
    int AutoFontMinPt,
    int AutoFontMaxPt,
    double AutoHeightRatio,
    int MinFontSizePt,
    int MaxFontSizePt
);

public sealed record SignatureCeilingsResponse(
    int MaxDocumentsPerRequest,
    int MaxSignersPerRequest,
    long MaxPdfBytes,
    int MaxPagesPerDocument,
    int MaxTokenHours,
    int MinRetentionYears,
    bool PurgeAllowed,
    IReadOnlyList<string> AllowedChannels
)
{
    public static SignatureCeilingsResponse From(SignatureLimits limits) =>
        new(
            limits.MaxDocumentsPerRequest,
            limits.MaxSignersPerRequest,
            limits.MaxPdfBytes,
            limits.MaxPagesPerDocument,
            limits.MaxTokenHours,
            limits.MinRetentionYears,
            limits.PurgeAllowed,
            ChannelNames(limits.AllowedChannels)
        );

    /// <summary>Canales como nombres (mismo formato que <c>GET /signature/settings</c>).</summary>
    public static IReadOnlyList<string> ChannelNames(VerificationChannel mask) =>
        Enum.GetValues<VerificationChannel>()
            .Where(channel => channel != VerificationChannel.None && mask.HasFlag(channel))
            .Select(channel => channel.ToString())
            .ToList();
}

public static class GetEffectiveSignatureLimitsHandler
{
    public static async Task<EffectiveSignatureLimitsResponse> Handle(
        GetEffectiveSignatureLimitsQuery query,
        ISignatureLimitsResolver limits,
        ISignaturePlatformSettingsProvider platform,
        ITenantSignatureSettingsRepository officeSettings,
        IStampStyleResolver stampStyles,
        CancellationToken ct
    )
    {
        var policy = await limits.ResolveAsync(query.TenantId, ct);
        var settings = await platform.GetAsync(ct);
        var style = await stampStyles.ResolveAsync(query.TenantId, ct);
        var dateFormat =
            (await officeSettings.GetByTenantIdAsync(query.TenantId, ct))?.DateStampFormat
            ?? DateStampFormat.MonthDayYear;
        return new EffectiveSignatureLimitsResponse(
            SignatureCeilingsResponse.From(policy.Ceilings),
            policy.ForRequests.MaxPdfBytes,
            policy.ForRequests.MaxPagesPerDocument,
            SignatureLimits.MinTokenHours,
            policy.DefaultTokenHours,
            policy.RetentionYears,
            RetentionPolicy.MaxRetentionYears,
            policy.PurgeEnabled,
            SignatureCeilingsResponse.ChannelNames(policy.ForRequests.AllowedChannels),
            policy.DefaultChannel.ToString(),
            settings.OtpLength,
            settings.OtpLifetimeMinutes,
            settings.OtpResendCooldownSeconds,
            settings.MaxSignatureImageBytes,
            settings.MaxProfilesPerPreparer,
            settings.MaxRolesPerTemplate,
            settings.MaxFieldsPerSigner,
            TenantSignatureSettings.MinReminderIntervalHours,
            TenantSignatureSettings.MaxReminderIntervalHours,
            DeliveryPolicy.MinLinkLifetimeDays,
            DeliveryPolicy.MaxLinkLifetimeDays,
            DeliveryPolicy.MinAttachmentBytes,
            DeliveryPolicy.MaxAttachmentBytesCeiling,
            style.ShowSignatureFrame,
            dateFormat.ToString(),
            style.DatePattern,
            style.AutoFontMinPt,
            style.AutoFontMaxPt,
            FieldTypography.AutoHeightRatio,
            FieldTypography.MinFontSizePt,
            FieldTypography.MaxFontSizePt
        );
    }
}
