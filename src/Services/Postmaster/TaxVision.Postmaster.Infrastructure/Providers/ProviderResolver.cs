using BuildingBlocks.Security;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Providers;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Infrastructure.Providers;

/// <summary>
/// Implementación de <see cref="IProviderResolver"/>. <see cref="ProviderScope.System"/> siempre
/// resuelve al <see cref="SystemEmailProvider"/> default; <see cref="ProviderScope.Tenant"/> exige un
/// <see cref="TenantEmailProvider"/> propio y jamás cae a System (plan §14.5, anti-spoofing).
/// </summary>
public sealed class ProviderResolver(
    ISystemEmailProviderRepository systemProviders,
    ITenantEmailProviderRepository tenantProviders,
    IProviderHealthStatusRepository healthStatuses,
    ISecretProtector secretProtector
) : IProviderResolver
{
    public Task<ResolveResult> ResolveAsync(
        Guid tenantId,
        ProviderScope requiredScope,
        ProviderPriorityHint? priorityHint,
        CancellationToken ct
    )
    {
        if (priorityHint == ProviderPriorityHint.ForceSystem)
            return ResolveSystemAsync(ct);

        return requiredScope switch
        {
            ProviderScope.System => ResolveSystemAsync(ct),
            ProviderScope.Tenant => ResolveTenantAsync(tenantId, ct),
            _ => throw new ArgumentOutOfRangeException(
                nameof(requiredScope),
                requiredScope,
                "Unsupported provider scope."
            ),
        };
    }

    private async Task<ResolveResult> ResolveSystemAsync(CancellationToken ct)
    {
        var lookup = await systemProviders.GetEnabledDefaultAsync(ct);
        return lookup.IsFailure
            ? new ResolveResult(ProviderResolutionStatus.SystemProviderMissing, null, lookup.Error.Message)
            : new ResolveResult(ProviderResolutionStatus.Resolved, ToResolvedSystemProvider(lookup.Value), null);
    }

    private async Task<ResolveResult> ResolveTenantAsync(Guid tenantId, CancellationToken ct)
    {
        var lookup = await tenantProviders.GetEnabledByTenantAsync(tenantId, ct);
        if (lookup.IsFailure)
            return new ResolveResult(ProviderResolutionStatus.ProviderNotConfigured, null, lookup.Error.Message);

        var provider = lookup.Value;
        var unhealthyReason = await CheckCircuitBreakerOpenAsync(tenantId, provider.ProviderCode, ct);
        return unhealthyReason is null
            ? new ResolveResult(ProviderResolutionStatus.Resolved, ToResolvedTenantProvider(provider), null)
            : new ResolveResult(ProviderResolutionStatus.ProviderUnhealthy, null, unhealthyReason);
    }

    private async Task<string?> CheckCircuitBreakerOpenAsync(Guid tenantId, string providerCode, CancellationToken ct)
    {
        var healthLookup = await healthStatuses.GetAsync(ProviderKind.Tenant, tenantId, providerCode, ct);
        if (healthLookup.IsFailure)
            return null;

        return healthLookup.Value.CircuitBreakerState == CircuitBreakerState.Open
            ? $"Provider '{providerCode}' circuit breaker is open for tenant {tenantId}."
            : null;
    }

    private ResolvedEmailProvider ToResolvedSystemProvider(SystemEmailProvider provider) =>
        new(
            provider.ProviderCode,
            provider.Host ?? string.Empty,
            provider.Port ?? 587,
            provider.UseTls,
            provider.Username,
            DecryptOrNull(provider.PasswordCipher),
            provider.FromAddressDefault,
            provider.FromDisplayNameDefault,
            provider.RateLimitPerMinute
        );

    private ResolvedEmailProvider ToResolvedTenantProvider(TenantEmailProvider provider) =>
        new(
            provider.ProviderCode,
            provider.Host ?? string.Empty,
            provider.Port ?? 587,
            provider.UseTls,
            provider.Username,
            DecryptOrNull(provider.PasswordCipher),
            provider.FromAddressDefault,
            provider.FromDisplayNameDefault,
            provider.RateLimitPerMinute
        );

    private string? DecryptOrNull(Domain.ValueObjects.EncryptedSecret? secret) =>
        secret is null ? null : secretProtector.Unprotect(secret.Cipher);
}
