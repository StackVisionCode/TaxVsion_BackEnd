using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Application.Abstractions.Sealing;
using TaxVision.Signature.Application.Platform;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Sealing;

public sealed class StampStyleResolver(
    ITenantSignatureSettingsRepository settings,
    ISignaturePlatformSettingsProvider platform
) : IStampStyleResolver
{
    public async Task<StampStyle> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        var tenantSettings = await settings.GetByTenantIdAsync(tenantId, ct);
        var platformSettings = await platform.GetAsync(ct);
        // Sin fila de ajustes (tenant recién creado): lo mismo que recibe una oficina nueva.
        return new StampStyle(
            tenantSettings?.ShowSignatureFrame ?? true,
            (tenantSettings?.DateStampFormat ?? DateStampFormat.MonthDayYear).Pattern(),
            platformSettings.AutoFontMinPt,
            platformSettings.AutoFontMaxPt
        );
    }
}
