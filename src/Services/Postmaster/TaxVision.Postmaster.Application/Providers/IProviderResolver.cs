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

/// <param name="EffectiveScope">
/// Por cuál transporte salió de verdad, que con <see cref="ProviderScope.TenantPreferred"/> ya no es
/// el que se pidió. Lo necesita el consumer para dos cosas: anunciar a la oficina en el From cuando
/// acabó saliendo por System, y guardar en <c>SentMessage</c> lo que pasó en vez de lo que se pidió —
/// si no, el historial de envíos diría "TenantPreferred" y nadie podría saber quién mandó el correo.
/// Null cuando no se resolvió nada.
/// </param>
public sealed record ResolveResult(
    ProviderResolutionStatus Status,
    ResolvedEmailProvider? Provider,
    string? Reason,
    ProviderScope? EffectiveScope = null
);

/// <summary>
/// Resuelve el provider a usar para un tenant + scope. Política estricta anti-spoofing (plan §14.5):
/// <see cref="ProviderScope.Tenant"/> sin <c>TenantEmailProvider</c> propio NUNCA cae a System —
/// devuelve <see cref="ProviderResolutionStatus.ProviderNotConfigured"/>.
/// <see cref="ProviderScope.TenantPreferred"/> sí puede caer, pero solo con
/// <paramref name="systemFallbackAllowed"/>.
/// </summary>
public interface IProviderResolver
{
    /// <param name="systemFallbackAllowed">
    /// Solo lo lee <see cref="ProviderScope.TenantPreferred"/>. Es un parámetro y no una regla
    /// interna porque depende de datos del envío que el resolver no ve (que traiga <c>ReplyTo</c> y
    /// que sea transaccional, no campaña) — meterlos acá obligaría a pasarle el evento entero.
    /// Sin default a propósito: cada caller tiene que decidirlo.
    /// </param>
    Task<ResolveResult> ResolveAsync(
        Guid tenantId,
        ProviderScope requiredScope,
        ProviderPriorityHint? priorityHint,
        bool systemFallbackAllowed,
        CancellationToken ct
    );
}
