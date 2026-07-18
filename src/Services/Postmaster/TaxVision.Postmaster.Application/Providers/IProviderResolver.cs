using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Providers;

namespace TaxVision.Postmaster.Application.Providers;

public enum ProviderResolutionStatus
{
    Resolved,
    ProviderNotConfigured,
    ProviderUnhealthy,
    SystemProviderMissing,
}

/// <summary>Anula la resolución normal para forzar System. La autorización de quién puede usarlo vive en el caller (Fase 5), no aquí.</summary>
public enum ProviderPriorityHint
{
    ForceSystem,
}

public sealed record ResolveResult(ProviderResolutionStatus Status, ResolvedEmailProvider? Provider, string? Reason);

/// <summary>
/// Resuelve el provider a usar para un tenant + scope. Política estricta anti-spoofing (plan §14.5):
/// <see cref="ProviderScope.Tenant"/> sin <c>TenantEmailProvider</c> propio NUNCA cae a System —
/// devuelve <see cref="ProviderResolutionStatus.ProviderNotConfigured"/>.
/// </summary>
public interface IProviderResolver
{
    Task<ResolveResult> ResolveAsync(
        Guid tenantId,
        ProviderScope requiredScope,
        ProviderPriorityHint? priorityHint,
        CancellationToken ct
    );
}
