using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Application.Limits;

public interface ITenantSignatureLimitsRepository
{
    Task<TenantSignatureLimits?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default);

    void Add(TenantSignatureLimits limits);
}

/// <summary>Lo que rige hoy para un tenant: plan proyectado + excepción de PlatformAdmin + elección de la oficina.</summary>
public interface ISignatureLimitsResolver
{
    Task<EffectiveSignaturePolicy> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}

public sealed class SignatureLimitsResolver(
    ITenantSignatureLimitsRepository planLimits,
    ITenantSignatureSettingsRepository settings
) : ISignatureLimitsResolver
{
    public async Task<EffectiveSignaturePolicy> ResolveAsync(Guid tenantId, CancellationToken ct = default)
    {
        // Sin evento del plan todavía: los valores vigentes antes de los entitlements.
        var plan = (await planLimits.GetByTenantIdAsync(tenantId, ct))?.ToLimits() ?? TenantSignatureLimits.Fallback();
        var tenantSettings = await settings.GetByTenantIdAsync(tenantId, ct);
        return tenantSettings?.EffectiveWithin(plan) ?? WithoutOfficeSettings(plan);
    }

    /// <summary>Tenant sin fila de ajustes (aún no llegó TenantCreated): los defaults de una oficina nueva.</summary>
    private static EffectiveSignaturePolicy WithoutOfficeSettings(SignatureLimits plan)
    {
        var documents = DocumentLimits.Default();
        var channels = TenantSignatureSettings.DefaultAllowedChannels & plan.AllowedChannels;
        if (channels == VerificationChannel.None)
            channels = plan.AllowedChannels;

        var forRequests = SignatureLimits.Create(
            plan.MaxDocumentsPerRequest,
            plan.MaxSignersPerRequest,
            Math.Min(documents.MaxPdfBytes, plan.MaxPdfBytes),
            Math.Min(documents.MaxPagesPerDocument, plan.MaxPagesPerDocument),
            plan.MaxTokenHours,
            plan.MinRetentionYears,
            plan.PurgeAllowed,
            channels
        );
        return new EffectiveSignaturePolicy(
            plan,
            forRequests,
            Math.Min(TenantSignatureSettings.DefaultTokenExpirationHours, plan.MaxTokenHours),
            Math.Max(RetentionPolicy.DefaultRetentionYears, plan.MinRetentionYears),
            false,
            channels.HasFlag(TenantSignatureSettings.DefaultPreselectedChannel)
                ? TenantSignatureSettings.DefaultPreselectedChannel
                : (VerificationChannel)((int)channels & -(int)channels), // el canal más bajo habilitado
            RetentionYearsSource.Signature
        );
    }
}
