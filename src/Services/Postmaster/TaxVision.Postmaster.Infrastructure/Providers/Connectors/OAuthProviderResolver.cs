using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Application.Providers;

namespace TaxVision.Postmaster.Infrastructure.Providers.Connectors;

/// <summary>
/// Consulta únicamente la proyección local <c>TenantOAuthAccount</c> (D3 §4.3) — nunca llama a
/// Connectors por red en esta resolución, mismo criterio que <c>ProviderResolver</c> con
/// <c>ITenantEmailProviderRepository</c>.
/// </summary>
public sealed class OAuthProviderResolver(ITenantOAuthAccountRepository accounts) : IOAuthProviderResolver
{
    public async Task<OAuthResolveResult> ResolveAsync(Guid tenantId, CancellationToken ct)
    {
        var account = await accounts.FindActiveByTenantIdAsync(tenantId, ct);
        if (account is null)
            return new OAuthResolveResult(
                OAuthResolutionStatus.ProviderNotConfigured,
                Provider: null,
                Reason: "No active OAuth account connected for this tenant."
            );

        return new OAuthResolveResult(
            OAuthResolutionStatus.Resolved,
            new ResolvedOAuthProvider(
                account.AccountId,
                account.ProviderCode,
                account.FromAddress,
                FromDisplayName: null
            ),
            Reason: null
        );
    }

    /// <summary>
    /// <see cref="ITenantOAuthAccountRepository.GetByAccountIdAsync"/> ya filtra por
    /// <c>TenantId</c>+<c>AccountId</c> juntos — una cuenta de otro tenant o inexistente vuelve
    /// <see langword="null"/> sin distinción, mismo criterio de "no reveles si existe" que el resto
    /// del repo aplica en lookups cross-tenant. <c>IsActive</c> se valida acá porque el repo la trae
    /// tal cual esté (a diferencia de <see cref="ResolveAsync"/>, que ya filtra activas en la query).
    /// </summary>
    public async Task<OAuthResolveResult> ResolveByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct)
    {
        var account = await accounts.GetByAccountIdAsync(tenantId, accountId, ct);
        if (account is null || !account.IsActive)
            return new OAuthResolveResult(
                OAuthResolutionStatus.ProviderNotConfigured,
                Provider: null,
                Reason: "The selected account is not connected or is not active for this tenant."
            );

        return new OAuthResolveResult(
            OAuthResolutionStatus.Resolved,
            new ResolvedOAuthProvider(
                account.AccountId,
                account.ProviderCode,
                account.FromAddress,
                FromDisplayName: null
            ),
            Reason: null
        );
    }
}
